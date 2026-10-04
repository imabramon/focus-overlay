using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FocusOverlay.Models;
using FocusOverlay.Properties;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FocusOverlay.Services;

public sealed record MarkdownIssue(int Line, string Message)
{
    public string Text => string.Format(Strings.DiagLine, Line + 1, Message);
}

public static class MarkdownDiagnostics
{
    private static readonly Regex _unparsedImagePattern = new(@"!\[[^\]\n]*\]\((?<url>[^)\n]*)\)", RegexOptions.Compiled);
    private static readonly Regex _titledUrlPattern = new(@"^\S+\s+(?:""[^""]*""|'[^']*'|\([^)]*\))$", RegexOptions.Compiled);

    public static IReadOnlyList<MarkdownIssue> Analyze(Preset preset, string? markdown)
    {
        var issues = new List<MarkdownIssue>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return issues;
        }

        var document = Markdown.Parse(markdown, MarkdownRenderer.Pipeline);
        foreach (var image in document.Descendants<LinkInline>().Where(link => link.IsImage))
        {
            var error = CheckImage(preset, image.Url);
            if (error != null)
            {
                issues.Add(new MarkdownIssue(image.Line, error));
            }
        }

        foreach (var block in document.Descendants<LeafBlock>().Where(block => block is not CodeBlock && block.Inline != null))
        {
            CheckUnparsedImages(block.Inline!, issues);
        }

        issues.AddRange(PageDisplaySettings.Validate(markdown));
        return issues.OrderBy(issue => issue.Line).ToList();
    }

    public static IReadOnlyList<MarkdownIssue> AnalyzeSystem(string? markdown) => PageDisplaySettings.ValidateSystem(markdown);

    private static string? CheckImage(Preset preset, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Strings.DiagImageNoFile;
        }

        var uri = StateStore.ResolveAsset(preset, url);
        if (uri == null || (uri.IsFile && !File.Exists(uri.LocalPath)))
        {
            return string.Format(Strings.DiagImageNotFound, url);
        }

        if (uri.IsFile && !StateStore.IsImageFile(uri.LocalPath))
        {
            return string.Format(Strings.DiagNotImage, url);
        }

        return null;
    }

    private static void CheckUnparsedImages(ContainerInline container, List<MarkdownIssue> issues)
    {
        var text = new StringBuilder();
        var lines = new List<(int Offset, int Line)>();
        CollectText(container, text, lines);

        foreach (Match match in _unparsedImagePattern.Matches(text.ToString()))
        {
            var url = match.Groups["url"].Value.Trim();
            if (!url.Any(char.IsWhiteSpace) || _titledUrlPattern.IsMatch(url))
            {
                continue;
            }

            var line = lines.LastOrDefault(entry => entry.Offset <= match.Index).Line;
            issues.Add(new MarkdownIssue(line, string.Format(Strings.DiagImagePathSpace, url)));
        }
    }

    private static void CollectText(ContainerInline container, StringBuilder text, List<(int Offset, int Line)> lines)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    lines.Add((text.Length, literal.Line));
                    text.Append(literal.Content.ToString());
                    break;
                case LineBreakInline:
                    text.Append('\n');
                    break;
                case LinkInline:
                    text.Append('\u0001');
                    break;
                case ContainerInline child:
                    CollectText(child, text, lines);
                    break;
                default:
                    text.Append('\u0001');
                    break;
            }
        }
    }
}
