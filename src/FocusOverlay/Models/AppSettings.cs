namespace FocusOverlay.Models;

public enum OverlayCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    TopCenter,
    BottomCenter,
}

public enum AppLanguage
{
    System,
    Russian,
    English,
}

public sealed class HotkeySettings
{
    public string Toggle { get; set; } = "Ctrl+Alt+O";
    public string NextPage { get; set; } = "Ctrl+Alt+Right";
    public string PrevPage { get; set; } = "Ctrl+Alt+Left";
    public string ZoomIn { get; set; } = "Ctrl+Alt+OemPlus";
    public string ZoomOut { get; set; } = "Ctrl+Alt+OemMinus";
    public string ScrollUp { get; set; } = "Ctrl+Alt+Up";
    public string ScrollDown { get; set; } = "Ctrl+Alt+Down";
    public string NextPreset { get; set; } = "Ctrl+Alt+P";
    public string PrevPreset { get; set; } = string.Empty;
    public string MoveTop { get; set; } = "Ctrl+Alt+Z";
    public string MoveLeft { get; set; } = "Ctrl+Alt+X";
    public string MoveCenter { get; set; } = "Ctrl+Alt+C";
    public string MoveRight { get; set; } = "Ctrl+Alt+V";
    public string MoveBottom { get; set; } = "Ctrl+Alt+B";
    public string ResetUserView { get; set; } = "Ctrl+Alt+R";
}

public sealed class TabStripSettings
{
    public bool Visible { get; set; } = true;
    public bool Detached { get; set; }
    public OverlayCorner Corner { get; set; } = OverlayCorner.TopLeft;
    public double OffsetX { get; set; } = 24;
    public double OffsetY { get; set; } = 24;
    public double Width { get; set; } = 420;
    public double Scale { get; set; } = 1.0;
    public double FontSize { get; set; } = 12;
    public double BackgroundOpacity { get; set; } = 0.35;
    public string TextColor { get; set; } = "#F0E6D2";
    public string AccentColor { get; set; } = "#E0A040";

    public TabStripSettings Clone() => (TabStripSettings)MemberwiseClone();
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
    public TabStripSettings TabStrip { get; set; } = new();
    public AppLanguage Language { get; set; } = AppLanguage.System;

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

    public AppSettings CloneDisplay() => new()
    {
        Corner = Corner,
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        Width = Width,
        Height = Height,
        FontSize = FontSize,
        BackgroundOpacity = BackgroundOpacity,
        TextColor = TextColor,
        AccentColor = AccentColor,
        Scale = Scale,
        OverlayVisible = OverlayVisible,
    };
}
