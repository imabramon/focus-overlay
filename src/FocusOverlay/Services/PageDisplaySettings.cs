using System;
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
        if (string.IsNullOrEmpty(markdown) || !markdown.Contains(Language, StringComparison.OrdinalIgnoreCase))
        {
            return effective;
        }

        var blocks = Markdown.Parse(markdown, _pipeline).Descendants<FencedCodeBlock>().Where(IsSettingsBlock);
        foreach (var block in blocks)
        {
            foreach (var line in block.Lines.Lines.Take(block.Lines.Count))
            {
                ApplyLine(effective, settings, line.Slice.ToString());
            }
        }

        return effective;
    }

    private static void ApplyLine(AppSettings target, AppSettings global, string line)
    {
        var text = line.Trim();
        if (text.Length == 0 || text.StartsWith('#') || text.StartsWith("//"))
        {
            return;
        }

        var separator = text.IndexOfAny([':', '=']);
        if (separator <= 0)
        {
            return;
        }

        var key = NormalizeKey(text[..separator]);
        var value = text[(separator + 1)..].Trim().Trim('"', '\'');

        switch (key)
        {
            case "corner":
            case "position":
                if (TryParseCorner(value, out var corner))
                {
                    target.Corner = corner;
                }

                break;
            case "offsetx":
            case "x":
                if (TryParseNumber(value, out var offsetX))
                {
                    target.OffsetX = offsetX;
                }

                break;
            case "offsety":
            case "y":
                if (TryParseNumber(value, out var offsetY))
                {
                    target.OffsetY = offsetY;
                }

                break;
            case "width":
                if (TryParseNumber(value, out var width))
                {
                    target.Width = Math.Max(150, width);
                }

                break;
            case "height":
                if (TryParseNumber(value, out var height))
                {
                    target.Height = Math.Max(100, height);
                }

                break;
            case "scale":
            case "zoom":
                if (TryParseNumber(value, out var scale) && scale > 0)
                {
                    target.Scale = Math.Clamp(global.Scale * scale, 0.25, 5);
                }

                break;
            case "fontsize":
            case "font":
                if (TryParseNumber(value, out var fontSize))
                {
                    target.FontSize = Math.Clamp(fontSize, 6, 48);
                }

                break;
            case "opacity":
            case "background":
            case "backgroundopacity":
                if (TryParseOpacity(value, out var opacity))
                {
                    target.BackgroundOpacity = opacity;
                }

                break;
            case "textcolor":
            case "color":
            case "text":
                if (IsColor(value))
                {
                    target.TextColor = value;
                }

                break;
            case "accentcolor":
            case "accent":
                if (IsColor(value))
                {
                    target.AccentColor = value;
                }

                break;
        }
    }

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
