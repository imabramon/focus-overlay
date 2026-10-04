using System;
using System.Collections.ObjectModel;

namespace FocusOverlay.Models;

public sealed class AppState
{
    public AppSettings Settings { get; set; } = new();
    public ObservableCollection<Preset> Presets { get; set; } = new();
    public TabViewSetting UserSetting { get; set; } = new();

    public Preset ActivePreset => Presets[Settings.ActivePresetIndex];

    public void Normalize()
    {
        Settings ??= new AppSettings();
        UserSetting ??= new TabViewSetting();
        Settings.Hotkeys ??= new HotkeySettings();
        Settings.Hotkeys.NextPreset ??= string.Empty;
        Settings.Hotkeys.PrevPreset ??= string.Empty;
        Settings.TabStrip ??= new TabStripSettings();
        Presets ??= new ObservableCollection<Preset>();

        if (Presets.Count == 0)
        {
            Presets.Add(CreateSamplePreset());
        }

        foreach (var preset in Presets)
        {
            preset.Pages ??= new ObservableCollection<OverlayPage>();
            preset.SystemContent ??= string.Empty;
            if (string.IsNullOrWhiteSpace(preset.Id))
            {
                preset.Id = Guid.NewGuid().ToString("N");
            }
        }

        Settings.ActivePresetIndex = Math.Clamp(Settings.ActivePresetIndex, 0, Presets.Count - 1);
        Settings.ActivePageIndex = Math.Clamp(Settings.ActivePageIndex, 0, Math.Max(0, ActivePreset.Pages.Count - 1));
        Settings.Scale = Math.Clamp(Settings.Scale, 0.5, 3.0);
        Settings.Width = Math.Max(150, Settings.Width);
        Settings.Height = Math.Max(100, Settings.Height);
        Settings.FontSize = Math.Clamp(Settings.FontSize, 6, 48);
        Settings.BackgroundOpacity = Math.Clamp(Settings.BackgroundOpacity, 0, 1);

        var strip = Settings.TabStrip;
        strip.Width = Math.Max(100, strip.Width);
        strip.Scale = Math.Clamp(strip.Scale, 0.25, 4);
        strip.FontSize = Math.Clamp(strip.FontSize, 6, 48);
        strip.BackgroundOpacity = Math.Clamp(strip.BackgroundOpacity, 0, 1);
    }

    public static AppState CreateDefault()
    {
        var state = new AppState();
        state.Presets.Add(CreateSamplePreset());
        return state;
    }

    private static Preset CreateSamplePreset()
    {
        var preset = new Preset { Name = "Шаблон" };
        preset.Pages.Add(new OverlayPage
        {
            Title = "README",
            Content = """
                # Focus Overlay

                - Двойной клик по иконке в трее — **редактор**
                - Правый клик — меню и **настройки**
                - `Ctrl+Alt+O` — показать / скрыть
                - `Ctrl+Alt+←` / `→` — вкладки
                - `Ctrl+Alt+P` — следующий пресет
                """,
        });
        return preset;
    }
}
