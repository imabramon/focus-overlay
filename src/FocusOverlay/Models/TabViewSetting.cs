using System;
using FocusOverlay.Services;

namespace FocusOverlay.Models;

public sealed class TabViewSetting
{
    public OverlayCorner? Corner { get; set; }
    public double? OffsetX { get; set; }
    public double? OffsetY { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public double? Scale { get; set; }
    public double? FontSize { get; set; }
    public double? BackgroundOpacity { get; set; }
    public string? TextColor { get; set; }
    public string? AccentColor { get; set; }

    public static TabViewSetting FromSettings(AppSettings settings) => new()
    {
        Corner = settings.Corner,
        OffsetX = settings.OffsetX,
        OffsetY = settings.OffsetY,
        Width = settings.Width,
        Height = settings.Height,
        Scale = settings.Scale,
        FontSize = settings.FontSize,
        BackgroundOpacity = settings.BackgroundOpacity,
        TextColor = settings.TextColor,
        AccentColor = settings.AccentColor,
    };

    public static TabViewSetting FromMarkdown(string? markdown) => PageDisplaySettings.Parse(markdown);

    public static TabView Resolve(TabViewSetting app, params TabViewSetting?[] layers)
    {
        var defaults = new AppSettings();
        var corner = app.Corner ?? defaults.Corner;
        var offsetX = app.OffsetX ?? defaults.OffsetX;
        var offsetY = app.OffsetY ?? defaults.OffsetY;
        var width = app.Width ?? defaults.Width;
        var height = app.Height ?? defaults.Height;
        var scale = app.Scale ?? defaults.Scale;
        var fontSize = app.FontSize ?? defaults.FontSize;
        var opacity = app.BackgroundOpacity ?? defaults.BackgroundOpacity;
        var textColor = app.TextColor ?? defaults.TextColor;
        var accentColor = app.AccentColor ?? defaults.AccentColor;

        foreach (var layer in layers)
        {
            if (layer == null)
            {
                continue;
            }

            corner = layer.Corner ?? corner;
            offsetX = layer.OffsetX ?? offsetX;
            offsetY = layer.OffsetY ?? offsetY;
            width = layer.Width ?? width;
            height = layer.Height ?? height;
            scale *= layer.Scale ?? 1;
            fontSize = layer.FontSize ?? fontSize;
            opacity = layer.BackgroundOpacity ?? opacity;
            textColor = layer.TextColor ?? textColor;
            accentColor = layer.AccentColor ?? accentColor;
        }

        return new TabView(
            corner,
            Math.Max(0, offsetX),
            Math.Max(0, offsetY),
            Math.Max(150, width),
            Math.Max(100, height),
            Math.Clamp(scale, 0.25, 5),
            Math.Clamp(fontSize, 6, 48),
            Math.Clamp(opacity, 0, 1),
            textColor,
            accentColor);
    }
}

public sealed record TabView(
    OverlayCorner Corner,
    double OffsetX,
    double OffsetY,
    double Width,
    double Height,
    double Scale,
    double FontSize,
    double BackgroundOpacity,
    string TextColor,
    string AccentColor);
