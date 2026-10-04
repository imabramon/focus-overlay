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
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToPrimary = 0x00000001;
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

        var handle = new WindowInteropHelper(this).EnsureHandle();
        var monitor = MonitorFromPoint(default, MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info) || GetDpiForMonitor(monitor, 0, out var dpi, out _) != 0)
        {
            Left = isRight ? SystemParameters.PrimaryScreenWidth - Width - offsetX : offsetX;
            Top = isBottom ? SystemParameters.PrimaryScreenHeight - Height - offsetY : offsetY;
            return;
        }

        var factor = dpi / 96.0;
        var width = (int)Math.Round(Width * factor);
        var height = (int)Math.Round(Height * factor);
        var bounds = info.Monitor;
        var x = isRight ? bounds.Right - width - (int)Math.Round(offsetX * factor) : bounds.Left + (int)Math.Round(offsetX * factor);
        var y = isBottom ? bounds.Bottom - height - (int)Math.Round(offsetY * factor) : bounds.Top + (int)Math.Round(offsetY * factor);

        var dpiChanges = GetDpiForWindow(handle) != dpi;
        SetWindowPos(handle, IntPtr.Zero, x, y, width, height, SwpNoZOrder | SwpNoActivate);
        if (dpiChanges)
        {
            SetWindowPos(handle, IntPtr.Zero, x, y, width, height, SwpNoZOrder | SwpNoActivate);
        }
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

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
