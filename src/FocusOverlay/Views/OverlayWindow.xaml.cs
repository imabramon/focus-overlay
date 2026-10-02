using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FocusOverlay.Models;
using FocusOverlay.Services;

namespace FocusOverlay.Views;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private OverlayPage? _renderedPage;
    private ScrollViewer? _scroll;

    public OverlayWindow()
    {
        InitializeComponent();
        _topmostTimer.Tick += (_, _) => EnsureTopmost();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                EnsureTopmost();
                _topmostTimer.Start();
            }
            else
            {
                _topmostTimer.Stop();
            }
        };
    }

    public void Apply(AppSettings settings, Preset preset, int pageIndex)
    {
        var textColor = ParseColor(settings.TextColor, Color.FromRgb(0xF0, 0xE6, 0xD2));
        var accentColor = ParseColor(settings.AccentColor, Color.FromRgb(0xE0, 0xA0, 0x40));
        var text = Freeze(new SolidColorBrush(textColor));
        var accent = Freeze(new SolidColorBrush(accentColor));

        RootScale.ScaleX = settings.Scale;
        RootScale.ScaleY = settings.Scale;
        Width = settings.Width * settings.Scale;
        Height = settings.Height * settings.Scale;
        FrameBorder.Background = Freeze(new SolidColorBrush(Color.FromArgb((byte)(settings.BackgroundOpacity * 255), 0, 0, 0)));

        var page = pageIndex >= 0 && pageIndex < preset.Pages.Count ? preset.Pages[pageIndex] : null;
        BuildTabs(preset, pageIndex, settings.FontSize, text, accent);

        var theme = new MarkdownTheme(
            settings.FontSize,
            text,
            accent,
            Freeze(new SolidColorBrush(WithAlpha(textColor, 0.7))),
            Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF))),
            Freeze(new SolidColorBrush(WithAlpha(accentColor, 0.4))),
            Math.Max(32, settings.Width - 30),
            source => StateStore.ResolveAsset(preset, source));

        var samePage = ReferenceEquals(page, _renderedPage);
        var offset = samePage ? GetScroll()?.VerticalOffset ?? 0 : 0;
        _renderedPage = page;

        var markdown = page?.Content ?? "*Нет вкладок — добавьте их в редакторе (двойной клик по иконке в трее).*";
        DocumentViewer.Document = MarkdownRenderer.Render(markdown, theme);
        Dispatcher.InvokeAsync(() => GetScroll()?.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);

        PlaceInCorner(settings);
    }

    public void ScrollBy(double delta)
    {
        var scroll = GetScroll();
        scroll?.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset + delta));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate);
    }

    private void BuildTabs(Preset preset, int activeIndex, double fontSize, Brush text, Brush accent)
    {
        TabStrip.Children.Clear();
        for (var i = 0; i < preset.Pages.Count; i++)
        {
            var isActive = i == activeIndex;
            TabStrip.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 4, 4),
                Background = isActive ? accent : new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                Child = new TextBlock
                {
                    Text = preset.Pages[i].Title,
                    FontSize = fontSize * 0.85,
                    FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = isActive ? Brushes.Black : text,
                },
            });
        }

        PageCounter.Text = preset.Pages.Count > 0 ? $"{activeIndex + 1}/{preset.Pages.Count}" : string.Empty;
        PageCounter.FontSize = fontSize * 0.85;
        PageCounter.Foreground = text;
    }

    private void PlaceInCorner(AppSettings settings)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var isRight = settings.Corner is OverlayCorner.TopRight or OverlayCorner.BottomRight;
        var isBottom = settings.Corner is OverlayCorner.BottomLeft or OverlayCorner.BottomRight;

        Left = isRight ? screenWidth - Width - settings.OffsetX : settings.OffsetX;
        Top = isBottom ? screenHeight - Height - settings.OffsetY : settings.OffsetY;
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

    private void EnsureTopmost()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
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

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return ColorConverter.ConvertFromString(value) is Color color ? color : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B);

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
