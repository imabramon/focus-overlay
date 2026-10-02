using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using FocusOverlay.Models;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FocusOverlay.Services;

public static class PresetFolderImporter
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UsePreciseSourceLocation()
        .Build();

    private static readonly IComparer<string?> _explorerOrder = Comparer<string?>.Create(StrCmpLogicalW);

    public static Preset Import(string folder)
    {
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        var markdownFiles = EnumerateVisibleFiles(root)
            .Where(relative => relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetDirectoryName, _explorerOrder)
            .ThenBy(Path.GetFileName, _explorerOrder)
            .ToList();

        if (markdownFiles.Count == 0)
        {
            throw new InvalidDataException("В папке нет .md файлов");
        }

        var preset = new Preset { Name = Path.GetFileName(root) };

        foreach (var relative in markdownFiles)
        {
            var content = File.ReadAllText(Path.Combine(root, relative), Encoding.UTF8);
            var directory = Path.GetDirectoryName(relative) ?? string.Empty;
            preset.Pages.Add(new OverlayPage
            {
                Title = Path.GetFileNameWithoutExtension(relative),
                Content = directory.Length > 0 ? RebaseImageLinks(content, root, directory) : content,
            });
        }

        CopyImages(root, preset);
        return preset;
    }

    private static IEnumerable<string> EnumerateVisibleFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .Where(relative => !relative
                .Split(Path.DirectorySeparatorChar)
                .Any(segment => segment.StartsWith('.')));

    private static void CopyImages(string root, Preset preset)
    {
        var assetsDirectory = StateStore.GetAssetsDirectory(preset);
        foreach (var relative in EnumerateVisibleFiles(root).Where(StateStore.IsImageFile))
        {
            var target = Path.Combine(assetsDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(root, relative), target, true);
        }
    }

    private static string RebaseImageLinks(string content, string root, string directory)
    {
        var document = Markdown.Parse(content, _pipeline);
        var replacements = new List<(int Start, int Length, string Url)>();

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!link.IsImage || string.IsNullOrWhiteSpace(link.Url) || link.UrlSpan.IsEmpty)
            {
                continue;
            }

            var url = link.Url;
            if (Uri.TryCreate(url, UriKind.Absolute, out _) || Path.IsPathRooted(url))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(root, directory, Uri.UnescapeDataString(url)));
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                continue;
            }

            var start = link.UrlSpan.Start;
            var length = link.UrlSpan.Length;
            if (start < 0 || start + length > content.Length)
            {
                continue;
            }

            var rebased = Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/').Replace(" ", "%20");
            replacements.Add((start, length, rebased));
        }

        var builder = new StringBuilder(content);
        foreach (var (start, length, url) in replacements.OrderByDescending(item => item.Start))
        {
            builder.Remove(start, length).Insert(start, url);
        }

        return builder.ToString();
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string? x, string? y);
}
