using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Media;
using FocusOverlay.Models;
using FocusOverlay.Properties;
using Markdig;
using Markdig.Syntax;

namespace FocusOverlay.Services;

public static class PageDisplaySettings
{
    public const string Language = "focus-overlay";

    public const string KeyCorner = "corner";
    public const string KeyOffsetX = "offset-x";
    public const string KeyOffsetY = "offset-y";
    public const string KeyWidth = "width";
    public const string KeyHeight = "height";
    public const string KeyScale = "scale";
    public const string KeyFontSize = "font-size";
    public const string KeyOpacity = "opacity";
    public const string KeyTextColor = "text-color";
    public const string KeyAccentColor = "accent-color";

    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().Build();

    public static bool IsSettingsBlock(FencedCodeBlock block) =>
        string.Equals(block.Info?.Trim(), Language, StringComparison.OrdinalIgnoreCase);

    public static TabViewSetting Parse(string? markdown)
    {
        var setting = new TabViewSetting();
        foreach (var (_, text) in EnumerateLines(markdown))
        {
            ApplyLine(setting, text);
        }

        return setting;
    }

    public static IReadOnlyList<MarkdownIssue> Validate(string? markdown)
    {
        var probe = new TabViewSetting();
        return EnumerateLines(markdown)
            .Select(line => (line.Line, Error: ApplyLine(probe, line.Text)))
            .Where(line => line.Error != null)
            .Select(line => new MarkdownIssue(line.Line, $"{Language}: {line.Error}"))
            .ToList();
    }

    public static IReadOnlyList<MarkdownIssue> ValidateSystem(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }

        var extra = Markdown.Parse(markdown, _pipeline)
            .Where(block => !(block is FencedCodeBlock fenced && IsSettingsBlock(fenced)))
            .Select(block => new MarkdownIssue(block.Line, string.Format(Strings.DiagSystemOnlyBlocks, Language)));

        return extra.Concat(Validate(markdown)).OrderBy(issue => issue.Line).ToList();
    }

    private static IEnumerable<(int Line, string Text)> EnumerateLines(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown) || !markdown.Contains(Language, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return Markdown.Parse(markdown, _pipeline)
            .Descendants<FencedCodeBlock>()
            .Where(IsSettingsBlock)
            .SelectMany(block => block.Lines.Lines.Take(block.Lines.Count))
            .Select(line => (line.Line, line.Slice.ToString()))
            .ToList();
    }

    public static string? ValidateValue(string key, string value) =>
        ApplyValue(new TabViewSetting(), key, key, value.Trim());

    public static (IReadOnlyDictionary<string, string> Entries, string Body) Split(string? markdown)
    {
        var entries = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(markdown) || !markdown.Contains(Language, StringComparison.OrdinalIgnoreCase))
        {
            return (entries, markdown ?? string.Empty);
        }

        var blocks = Markdown.Parse(markdown, _pipeline)
            .Descendants<FencedCodeBlock>()
            .Where(IsSettingsBlock)
            .ToList();

        foreach (var line in blocks.SelectMany(block => block.Lines.Lines.Take(block.Lines.Count)))
        {
            var text = line.Slice.ToString().Trim();
            if (!IsComment(text) && TrySplitLine(text, out var name, out var value) && CanonicalKey(name) is { } key)
            {
                entries[key] = key == KeyCorner && TryParseCorner(value, out var corner) ? CornerName(corner) : value;
            }
        }

        var body = new StringBuilder(markdown);
        foreach (var block in blocks.OrderByDescending(block => block.Span.Start))
        {
            var end = Math.Min(block.Span.End + 1, body.Length);
            while (end < body.Length && body[end] is '\r' or '\n')
            {
                end++;
            }

            body.Remove(block.Span.Start, end - block.Span.Start);
        }

        return (entries, body.ToString());
    }

    public static string Compose(IEnumerable<KeyValuePair<string, string>> entries, string body)
    {
        var lines = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .Select(entry => $"{entry.Key}: {entry.Value.Trim()}")
            .ToList();

        if (lines.Count == 0)
        {
            return body;
        }

        var newLine = Environment.NewLine;
        var block = $"```{Language}{newLine}{string.Join(newLine, lines)}{newLine}```";
        return string.IsNullOrEmpty(body) ? block : block + newLine + newLine + body;
    }

    public static string CornerName(OverlayCorner corner) => corner switch
    {
        OverlayCorner.TopLeft => "top-left",
        OverlayCorner.TopCenter => "top-center",
        OverlayCorner.TopRight => "top-right",
        OverlayCorner.BottomLeft => "bottom-left",
        OverlayCorner.BottomCenter => "bottom-center",
        _ => "bottom-right",
    };

    private static string? ApplyLine(TabViewSetting target, string line)
    {
        var text = line.Trim();
        if (IsComment(text))
        {
            return null;
        }

        if (!TrySplitLine(text, out var name, out var value))
        {
            return string.Format(Strings.DiagNotKeyValue, text);
        }

        var key = CanonicalKey(name);
        return key == null ? string.Format(Strings.DiagUnknownParameter, name) : ApplyValue(target, key, name, value);
    }

    private static bool IsComment(string text) => text.Length == 0 || text.StartsWith('#') || text.StartsWith("//");

    private static bool TrySplitLine(string text, out string name, out string value)
    {
        var separator = text.IndexOfAny([':', '=']);
        if (separator <= 0)
        {
            name = value = string.Empty;
            return false;
        }

        name = text[..separator].Trim();
        value = text[(separator + 1)..].Trim().Trim('"', '\'');
        return true;
    }

    private static string? CanonicalKey(string name) => NormalizeKey(name) switch
    {
        "corner" or "position" => KeyCorner,
        "offsetx" or "x" => KeyOffsetX,
        "offsety" or "y" => KeyOffsetY,
        "width" => KeyWidth,
        "height" => KeyHeight,
        "scale" or "zoom" => KeyScale,
        "fontsize" or "font" => KeyFontSize,
        "opacity" or "background" or "backgroundopacity" => KeyOpacity,
        "textcolor" or "color" or "text" => KeyTextColor,
        "accentcolor" or "accent" => KeyAccentColor,
        _ => null,
    };

    private static string? ApplyValue(TabViewSetting target, string key, string name, string value)
    {
        switch (key)
        {
            case KeyCorner:
                if (!TryParseCorner(value, out var corner))
                {
                    return Invalid(name, value, Strings.DiagExpectedCorner);
                }

                target.Corner = corner;
                return null;
            case KeyOffsetX:
                if (!TryParseNumber(value, out var offsetX))
                {
                    return Invalid(name, value, Strings.DiagExpectedPixels);
                }

                target.OffsetX = offsetX;
                return null;
            case KeyOffsetY:
                if (!TryParseNumber(value, out var offsetY))
                {
                    return Invalid(name, value, Strings.DiagExpectedPixels);
                }

                target.OffsetY = offsetY;
                return null;
            case KeyWidth:
                if (!TryParseNumber(value, out var width))
                {
                    return Invalid(name, value, Strings.DiagExpectedPixels);
                }

                target.Width = width;
                return null;
            case KeyHeight:
                if (!TryParseNumber(value, out var height))
                {
                    return Invalid(name, value, Strings.DiagExpectedPixels);
                }

                target.Height = height;
                return null;
            case KeyScale:
                if (!TryParseNumber(value, out var scale) || scale <= 0)
                {
                    return Invalid(name, value, Strings.DiagExpectedPositive);
                }

                target.Scale = scale;
                return null;
            case KeyFontSize:
                if (!TryParseNumber(value, out var fontSize))
                {
                    return Invalid(name, value, Strings.DiagExpectedFontSize);
                }

                target.FontSize = fontSize;
                return null;
            case KeyOpacity:
                if (!TryParseOpacity(value, out var opacity))
                {
                    return Invalid(name, value, Strings.DiagExpectedOpacity);
                }

                target.BackgroundOpacity = opacity;
                return null;
            case KeyTextColor:
                if (!IsColor(value))
                {
                    return Invalid(name, value, Strings.DiagExpectedColor);
                }

                target.TextColor = value;
                return null;
            case KeyAccentColor:
                if (!IsColor(value))
                {
                    return Invalid(name, value, Strings.DiagExpectedColor);
                }

                target.AccentColor = value;
                return null;
            default:
                return string.Format(Strings.DiagUnknownParameter, name);
        }
    }

    private static string Invalid(string name, string value, string expected) =>
        string.Format(Strings.DiagInvalidValue, value, name, expected);

    private static string NormalizeKey(string key) =>
        new(key.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static bool TryParseCorner(string value, out OverlayCorner corner)
    {
        corner = NormalizeKey(value) switch
        {
            "topleft" or "lefttop" or "tl" => OverlayCorner.TopLeft,
            "topright" or "righttop" or "tr" => OverlayCorner.TopRight,
            "bottomleft" or "leftbottom" or "bl" => OverlayCorner.BottomLeft,
            "bottomright" or "rightbottom" or "br" => OverlayCorner.BottomRight,
            "topcenter" or "centertop" or "top" or "tc" => OverlayCorner.TopCenter,
            "bottomcenter" or "centerbottom" or "bottom" or "bc" => OverlayCorner.BottomCenter,
            _ => (OverlayCorner)(-1),
        };
        return Enum.IsDefined(corner);
    }

    private static bool TryParseOpacity(string value, out double opacity)
    {
        var isPercent = value.EndsWith('%');
        if (!TryParseNumber(isPercent ? value[..^1] : value, out opacity))
        {
            return false;
        }

        if (isPercent || opacity > 1)
        {
            opacity /= 100;
        }

        opacity = Math.Clamp(opacity, 0, 1);
        return true;
    }

    private static bool TryParseNumber(string value, out double number) =>
        double.TryParse(value.Replace("px", string.Empty).Replace(',', '.').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
        && number >= 0;

    private static bool IsColor(string value)
    {
        try
        {
            return ColorConverter.ConvertFromString(value) is Color;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
