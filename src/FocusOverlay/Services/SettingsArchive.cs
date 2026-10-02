using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusOverlay.Models;

namespace FocusOverlay.Services;

public static class SettingsArchive
{
    public const string FileExtension = ".json";
    public const string DialogFilter = "Настройки Focus Overlay (*.json)|*.json|Все файлы (*.*)|*.*";

    private const string FormatId = "focus-overlay-settings";
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Export(AppSettings settings, string path)
    {
        var file = new SettingsFile
        {
            Format = FormatId,
            Version = FormatVersion,
            Overlay = OverlayLayout.From(settings),
            Hotkeys = CloneHotkeys(settings.Hotkeys),
            TabStrip = settings.TabStrip.Clone(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(file, _options), new UTF8Encoding(false));
    }

    public static void Import(string path, AppSettings target)
    {
        var file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(path), _options)
            ?? throw new InvalidDataException("Файл пуст");

        if (file.Format != FormatId)
        {
            throw new InvalidDataException("Файл не является настройками Focus Overlay");
        }

        if (file.Version > FormatVersion)
        {
            throw new InvalidDataException($"Настройки созданы более новой версией (формат v{file.Version})");
        }

        (file.Overlay ?? new OverlayLayout()).ApplyTo(target);
        target.Hotkeys = CloneHotkeys(file.Hotkeys ?? new HotkeySettings());
        target.TabStrip = file.TabStrip?.Clone() ?? new TabStripSettings();
    }

    public static void ResetToDefaults(AppSettings target)
    {
        OverlayLayout.From(new AppSettings()).ApplyTo(target);
        target.Hotkeys = new HotkeySettings();
        target.TabStrip = new TabStripSettings();
    }

    private static HotkeySettings CloneHotkeys(HotkeySettings source) => new()
    {
        Toggle = source.Toggle,
        NextPage = source.NextPage,
        PrevPage = source.PrevPage,
        ZoomIn = source.ZoomIn,
        ZoomOut = source.ZoomOut,
        ScrollUp = source.ScrollUp,
        ScrollDown = source.ScrollDown,
    };

    private sealed class SettingsFile
    {
        public string Format { get; set; } = string.Empty;
        public int Version { get; set; }
        public OverlayLayout? Overlay { get; set; }
        public HotkeySettings? Hotkeys { get; set; }
        public TabStripSettings? TabStrip { get; set; }
    }

    private sealed class OverlayLayout
    {
        private static readonly AppSettings _defaults = new();

        public OverlayCorner Corner { get; set; } = _defaults.Corner;
        public double OffsetX { get; set; } = _defaults.OffsetX;
        public double OffsetY { get; set; } = _defaults.OffsetY;
        public double Width { get; set; } = _defaults.Width;
        public double Height { get; set; } = _defaults.Height;
        public double Scale { get; set; } = _defaults.Scale;
        public double FontSize { get; set; } = _defaults.FontSize;
        public double BackgroundOpacity { get; set; } = _defaults.BackgroundOpacity;
        public string TextColor { get; set; } = _defaults.TextColor;
        public string AccentColor { get; set; } = _defaults.AccentColor;

        public static OverlayLayout From(AppSettings settings) => new()
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

        public void ApplyTo(AppSettings settings)
        {
            settings.Corner = Corner;
            settings.OffsetX = OffsetX;
            settings.OffsetY = OffsetY;
            settings.Width = Width;
            settings.Height = Height;
            settings.Scale = Scale;
            settings.FontSize = FontSize;
            settings.BackgroundOpacity = BackgroundOpacity;
            settings.TextColor = TextColor;
            settings.AccentColor = AccentColor;
        }
    }
}
