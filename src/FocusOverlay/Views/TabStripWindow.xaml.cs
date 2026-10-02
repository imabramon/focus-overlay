using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FocusOverlay.Models;

namespace FocusOverlay.Views;

public partial class TabStripWindow : ClickThroughWindow
{
    public TabStripWindow()
    {
        InitializeComponent();
    }

    public bool HasTabs { get; private set; }

    public double Apply(AppSettings settings, Preset preset, int activeIndex)
    {
        var strip = settings.TabStrip;
        var text = FrozenBrush(ParseColor(strip.TextColor, Color.FromRgb(0xF0, 0xE6, 0xD2)));
        var accent = FrozenBrush(ParseColor(strip.AccentColor, Color.FromRgb(0xE0, 0xA0, 0x40)));
        var inactive = FrozenBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        var fontSize = strip.FontSize;
        var scale = settings.Scale * strip.Scale;

        RootScale.ScaleX = scale;
        RootScale.ScaleY = scale;
        StripBorder.Background = FrozenBrush(Color.FromArgb((byte)(strip.BackgroundOpacity * 255), 0, 0, 0));

        TabStrip.Children.Clear();
        for (var i = 0; i < preset.Pages.Count; i++)
        {
            var isActive = i == activeIndex;
            TabStrip.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 4, 4),
                Background = isActive ? accent : inactive,
                Child = new TextBlock
                {
                    Text = preset.Pages[i].Title,
                    FontSize = fontSize,
                    FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = isActive ? Brushes.Black : text,
                },
            });
        }

        HasTabs = preset.Pages.Count > 0;
        PageCounter.Text = HasTabs ? $"{activeIndex + 1}/{preset.Pages.Count}" : string.Empty;
        PageCounter.FontSize = fontSize;
        PageCounter.Foreground = text;

        Width = strip.Detached ? strip.Width * scale : settings.Width * settings.Scale;
        RootGrid.Measure(new Size(Width, double.PositiveInfinity));
        Height = Math.Ceiling(RootGrid.DesiredSize.Height);

        if (strip.Detached)
        {
            PlaceInCorner(strip.Corner, strip.OffsetX, strip.OffsetY);
        }
        else
        {
            PlaceInCorner(settings.Corner, settings.OffsetX, settings.OffsetY);
        }

        return HasTabs ? Height : 0;
    }
}
