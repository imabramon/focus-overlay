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

    public void Apply(TabView view, Preset preset, OverlayPage? page, double minOffsetY)
    {
        var textColor = ParseColor(view.TextColor, DefaultTextColor);
        var accentColor = ParseColor(view.AccentColor, DefaultAccentColor);
        var offsetY = Math.Max(view.OffsetY, minOffsetY);
        var shift = offsetY - view.OffsetY;

        RootScale.ScaleX = view.Scale;
        RootScale.ScaleY = view.Scale;
        Width = view.Width * view.Scale;
        Height = Math.Max(MinContentHeight, view.Height * view.Scale - shift);
        FrameBorder.Background = FrozenBrush(Color.FromArgb((byte)(view.BackgroundOpacity * 255), 0, 0, 0));

        var theme = new MarkdownTheme(
            view.FontSize,
            FrozenBrush(textColor),
            FrozenBrush(accentColor),
            FrozenBrush(WithAlpha(textColor, 0.7)),
            FrozenBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            FrozenBrush(WithAlpha(accentColor, 0.4)),
            Math.Max(32, view.Width - 30),
            source => StateStore.ResolveAsset(preset, source));

        var samePage = ReferenceEquals(page, _renderedPage);
        var offset = samePage ? GetScroll()?.VerticalOffset ?? 0 : 0;
        _renderedPage = page;

        var markdown = page?.Content ?? "*Нет вкладок — добавьте их в редакторе (двойной клик по иконке в трее).*";
        DocumentViewer.Document = MarkdownRenderer.Render(markdown, theme);
        Dispatcher.InvokeAsync(() => GetScroll()?.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);

        PlaceInCorner(view.Corner, view.OffsetX, offsetY);
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
