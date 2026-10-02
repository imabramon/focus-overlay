using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FocusOverlay.Models;

namespace FocusOverlay.Views;

public class ClickThroughWindow : Window
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

    public ClickThroughWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;

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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate);
    }

    protected void PlaceInCorner(OverlayCorner corner, double offsetX, double offsetY)
    {
        var isRight = corner is OverlayCorner.TopRight or OverlayCorner.BottomRight;
        var isBottom = corner is OverlayCorner.BottomLeft or OverlayCorner.BottomRight;

        Left = isRight ? SystemParameters.PrimaryScreenWidth - Width - offsetX : offsetX;
        Top = isBottom ? SystemParameters.PrimaryScreenHeight - Height - offsetY : offsetY;
    }

    protected static Color ParseColor(string value, Color fallback)
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

    protected static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B);

    protected static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    protected static Color TextColorOf(AppSettings settings) =>
        ParseColor(settings.TextColor, Color.FromRgb(0xF0, 0xE6, 0xD2));

    protected static Color AccentColorOf(AppSettings settings) =>
        ParseColor(settings.AccentColor, Color.FromRgb(0xE0, 0xA0, 0x40));

    protected static SolidColorBrush BackgroundOf(AppSettings settings) =>
        FrozenBrush(Color.FromArgb((byte)(settings.BackgroundOpacity * 255), 0, 0, 0));

    private void EnsureTopmost()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
