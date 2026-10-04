using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using FocusOverlay.Models;
using Markdig;
using Markdig.Syntax;

namespace FocusOverlay.Services;

public static class PageDisplaySettings
{
    public const string Language = "focus-overlay";

    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().Build();

    public static bool IsSettingsBlock(FencedCodeBlock block) =>
        string.Equals(block.Info?.Trim(), Language, StringComparison.OrdinalIgnoreCase);

    public static AppSettings Resolve(AppSettings settings, string? markdown)
    {
        var effective = settings.CloneDisplay();
        foreach (var (_, text) in EnumerateLines(markdown))
        {
            ApplyLine(effective, settings, text);
        }

        return effective;
    }

    public static IReadOnlyList<MarkdownIssue> Validate(string? markdown)
    {
        var probe = new AppSettings();
        return EnumerateLines(markdown)
            .Select(line => (line.Line, Error: ApplyLine(probe, probe, line.Text)))
            .Where(line => line.Error != null)
            .Select(line => new MarkdownIssue(line.Line, $"{Language}: {line.Error}"))
            .ToList();
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

    private static string? ApplyLine(AppSettings target, AppSettings global, string line)
    {
        var text = line.Trim();
        if (text.Length == 0 || text.StartsWith('#') || text.StartsWith("//"))
        {
            return null;
        }

        var separator = text.IndexOfAny([':', '=']);
        if (separator <= 0)
        {
            return $"строка «{text}» не в формате «параметр: значение»";
        }

        var name = text[..separator].Trim();
        var value = text[(separator + 1)..].Trim().Trim('"', '\'');

        switch (NormalizeKey(name))
        {
            case "corner":
            case "position":
                if (!TryParseCorner(value, out var corner))
                {
                    return Invalid(name, value, "top-left, top-right, bottom-left или bottom-right");
                }

                target.Corner = corner;
                return null;
            case "offsetx":
            case "x":
                if (!TryParseNumber(value, out var offsetX))
                {
                    return Invalid(name, value, "число пикселей");
                }

                target.OffsetX = offsetX;
                return null;
            case "offsety":
            case "y":
                if (!TryParseNumber(value, out var offsetY))
                {
                    return Invalid(name, value, "число пикселей");
                }

                target.OffsetY = offsetY;
                return null;
            case "width":
                if (!TryParseNumber(value, out var width))
                {
                    return Invalid(name, value, "число пикселей");
                }

                target.Width = Math.Max(150, width);
                return null;
            case "height":
                if (!TryParseNumber(value, out var height))
                {
                    return Invalid(name, value, "число пикселей");
                }

                target.Height = Math.Max(100, height);
                return null;
            case "scale":
            case "zoom":
                if (!TryParseNumber(value, out var scale) || scale <= 0)
                {
                    return Invalid(name, value, "число больше 0, например 1.2");
                }

                target.Scale = Math.Clamp(global.Scale * scale, 0.25, 5);
                return null;
            case "fontsize":
            case "font":
                if (!TryParseNumber(value, out var fontSize))
                {
                    return Invalid(name, value, "число от 6 до 48");
                }

                target.FontSize = Math.Clamp(fontSize, 6, 48);
                return null;
            case "opacity":
            case "background":
            case "backgroundopacity":
                if (!TryParseOpacity(value, out var opacity))
                {
                    return Invalid(name, value, "0.5 или 50%");
                }

                target.BackgroundOpacity = opacity;
                return null;
            case "textcolor":
            case "color":
            case "text":
                if (!IsColor(value))
                {
                    return Invalid(name, value, "цвет: #RRGGBB, #AARRGGBB или имя");
                }

                target.TextColor = value;
                return null;
            case "accentcolor":
            case "accent":
                if (!IsColor(value))
                {
                    return Invalid(name, value, "цвет: #RRGGBB, #AARRGGBB или имя");
                }

                target.AccentColor = value;
                return null;
            default:
                return $"неизвестный параметр «{name}»";
        }
    }

    private static string Invalid(string name, string value, string expected) =>
        $"неверное значение «{value}» у «{name}», ожидается {expected}";

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
