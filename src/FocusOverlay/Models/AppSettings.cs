namespace FocusOverlay.Models;

public enum OverlayCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

public sealed class HotkeySettings
{
    public string Toggle { get; set; } = "Ctrl+Alt+O";
    public string NextPage { get; set; } = "Ctrl+Alt+PageDown";
    public string PrevPage { get; set; } = "Ctrl+Alt+PageUp";
    public string ZoomIn { get; set; } = "Ctrl+Alt+OemPlus";
    public string ZoomOut { get; set; } = "Ctrl+Alt+OemMinus";
    public string ScrollUp { get; set; } = "Ctrl+Alt+OemOpenBrackets";
    public string ScrollDown { get; set; } = "Ctrl+Alt+OemCloseBrackets";
}

public sealed class AppSettings : ObservableObject
{
    private double _scale = 1.0;
    private bool _overlayVisible = true;

    public OverlayCorner Corner { get; set; } = OverlayCorner.TopLeft;
    public double OffsetX { get; set; } = 24;
    public double OffsetY { get; set; } = 80;
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 560;
    public double FontSize { get; set; } = 14;
    public double BackgroundOpacity { get; set; } = 0.35;
    public string TextColor { get; set; } = "#F0E6D2";
    public string AccentColor { get; set; } = "#E0A040";
    public int ActivePresetIndex { get; set; }
    public int ActivePageIndex { get; set; }
    public HotkeySettings Hotkeys { get; set; } = new();

    public double Scale
    {
        get => _scale;
        set => SetField(ref _scale, value);
    }

    public bool OverlayVisible
    {
        get => _overlayVisible;
        set => SetField(ref _overlayVisible, value);
    }
}
