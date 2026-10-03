using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FocusOverlay.Models;
using FocusOverlay.Services;

namespace FocusOverlay.Views;

public partial class OverlayWindow : ClickThroughWindow
{
    private const double MinContentHeight = 60;

    private OverlayPage? _renderedPage;
    private ScrollViewer? _scroll;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    public void Apply(AppSettings settings, Preset preset, OverlayPage? page, double minOffsetY)
    {
        var textColor = TextColorOf(settings);
        var accentColor = AccentColorOf(settings);
        var offsetY = Math.Max(settings.OffsetY, minOffsetY);
        var shift = offsetY - settings.OffsetY;

        RootScale.ScaleX = settings.Scale;
        RootScale.ScaleY = settings.Scale;
        Width = settings.Width * settings.Scale;
        Height = Math.Max(MinContentHeight, settings.Height * settings.Scale - shift);
        FrameBorder.Background = BackgroundOf(settings);

        var theme = new MarkdownTheme(
            settings.FontSize,
            FrozenBrush(textColor),
            FrozenBrush(accentColor),
            FrozenBrush(WithAlpha(textColor, 0.7)),
            FrozenBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            FrozenBrush(WithAlpha(accentColor, 0.4)),
            Math.Max(32, settings.Width - 30),
            source => StateStore.ResolveAsset(preset, source));

        var samePage = ReferenceEquals(page, _renderedPage);
        var offset = samePage ? GetScroll()?.VerticalOffset ?? 0 : 0;
        _renderedPage = page;

        var markdown = page?.Content ?? "*Нет вкладок — добавьте их в редакторе (двойной клик по иконке в трее).*";
        DocumentViewer.Document = MarkdownRenderer.Render(markdown, theme);
        Dispatcher.InvokeAsync(() => GetScroll()?.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);

        PlaceInCorner(settings.Corner, settings.OffsetX, offsetY);
    }

    public void ScrollBy(double delta)
    {
        var scroll = GetScroll();
        scroll?.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset + delta));
    }

    private ScrollViewer? GetScroll()
    {
        if (_scroll == null)
        {
            DocumentViewer.ApplyTemplate();
            _scroll = FindDescendant<ScrollViewer>(DocumentViewer);
        }

        return _scroll;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}
