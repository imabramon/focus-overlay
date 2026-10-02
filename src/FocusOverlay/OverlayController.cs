using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using FocusOverlay.Models;
using FocusOverlay.Services;
using FocusOverlay.Views;
using Microsoft.Win32;

namespace FocusOverlay;

public sealed class OverlayController : IDisposable
{
    private const double ZoomStep = 0.1;

    private readonly AppState _state;
    private readonly OverlayWindow _overlay = new();
    private readonly TabStripWindow _tabStrip = new();
    private readonly HotkeyManager _hotkeys = new();
    private readonly TrayIcon _tray;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private EditorWindow? _editor;
    private SettingsWindow? _settingsWindow;

    public OverlayController()
    {
        _state = StateStore.Load();
        _tray = new TrayIcon(this);
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            Refresh();
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
    }

    public event Action? ActiveChanged;

    public event Action? SettingsReplaced;

    public AppState State => _state;

    public AppSettings Settings => _state.Settings;

    public void Start()
    {
        var failed = RegisterHotkeys();
        Refresh();

        if (failed.Count > 0)
        {
            _tray.ShowWarning("Горячие клавиши заняты", string.Join(", ", failed));
        }
    }

    public IReadOnlyList<string> RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var hotkeys = Settings.Hotkeys;
        var bindings = new (string Gesture, Action Action, bool Repeat)[]
        {
            (hotkeys.Toggle, Toggle, false),
            (hotkeys.NextPage, () => ChangePage(1), false),
            (hotkeys.PrevPage, () => ChangePage(-1), false),
            (hotkeys.ZoomIn, () => Zoom(ZoomStep), true),
            (hotkeys.ZoomOut, () => Zoom(-ZoomStep), true),
            (hotkeys.ScrollUp, () => _overlay.ScrollBy(-Settings.FontSize * 4), true),
            (hotkeys.ScrollDown, () => _overlay.ScrollBy(Settings.FontSize * 4), true),
        };

        return bindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.Gesture) && !_hotkeys.Register(binding.Gesture, binding.Action, binding.Repeat))
            .Select(binding => binding.Gesture)
            .ToList();
    }

    public void SuspendHotkeys() => _hotkeys.UnregisterAll();

    public void Toggle()
    {
        Settings.OverlayVisible = !Settings.OverlayVisible;
        Refresh();
        ScheduleSave();
    }

    public void SelectPreset(int index)
    {
        if (index < 0 || index >= _state.Presets.Count)
        {
            return;
        }

        Settings.ActivePresetIndex = index;
        Settings.ActivePageIndex = 0;
        ApplyActiveChange();
    }

    public void SelectPage(int index)
    {
        Settings.ActivePageIndex = index;
        ApplyActiveChange();
    }

    public void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
        ScheduleSave();
    }

    public void Refresh()
    {
        _state.Normalize();
        var preset = _state.ActivePreset;
        var pageIndex = Settings.ActivePageIndex;
        var page = pageIndex < preset.Pages.Count ? preset.Pages[pageIndex] : null;
        var effective = PageDisplaySettings.Resolve(Settings, page?.Content);
        var strip = Settings.TabStrip;
        var stripHeight = strip.Visible ? _tabStrip.Apply(Settings, preset, pageIndex) : 0;
        var sharesAnchor = !strip.Detached
            && effective.Corner == Settings.Corner
            && effective.OffsetX.Equals(Settings.OffsetX)
            && effective.OffsetY.Equals(Settings.OffsetY);
        _overlay.Apply(effective, preset, page, sharesAnchor ? stripHeight : 0);
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (!Settings.OverlayVisible)
        {
            _overlay.Hide();
            _tabStrip.Hide();
            return;
        }

        _overlay.Show();
        if (Settings.TabStrip.Visible && _tabStrip.HasTabs)
        {
            _tabStrip.Show();
        }
        else
        {
            _tabStrip.Hide();
        }
    }

    public void OpenEditor()
    {
        if (_editor == null)
        {
            _editor = new EditorWindow(this);
            _editor.Closed += (_, _) =>
            {
                _editor = null;
                RegisterHotkeys();
                Save();
            };
            _editor.Show();
        }

        if (_editor.WindowState == WindowState.Minimized)
        {
            _editor.WindowState = WindowState.Normal;
        }

        _editor.Activate();
    }

    public void OpenSettings()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                RegisterHotkeys();
                Save();
            };
            _settingsWindow.Show();
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
    }

    public void ImportSettings(Window? owner)
    {
        var dialog = new OpenFileDialog { Filter = SettingsArchive.DialogFilter, Title = "Импорт настроек" };
        if (!ShowDialog(dialog, owner))
        {
            return;
        }

        try
        {
            SettingsArchive.Import(dialog.FileName, Settings);
            ApplySettingsReplacement(owner);
        }
        catch (Exception ex)
        {
            ShowError(owner, $"Не удалось импортировать настройки:\n{ex.Message}");
        }
    }

    public void ExportSettings(Window? owner)
    {
        var dialog = new SaveFileDialog
        {
            Filter = SettingsArchive.DialogFilter,
            Title = "Экспорт настроек",
            FileName = "focus-overlay-settings" + SettingsArchive.FileExtension,
            DefaultExt = SettingsArchive.FileExtension,
        };

        if (!ShowDialog(dialog, owner))
        {
            return;
        }

        try
        {
            SettingsArchive.Export(Settings, dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError(owner, $"Не удалось экспортировать настройки:\n{ex.Message}");
        }
    }

    public void ResetSettings()
    {
        SettingsArchive.ResetToDefaults(Settings);
        ApplySettingsReplacement(_settingsWindow);
    }

    public void ImportPreset(Window? owner)
    {
        var dialog = new OpenFileDialog { Filter = PresetArchive.DialogFilter, Title = "Импорт пресета" };
        if (!ShowDialog(dialog, owner))
        {
            return;
        }

        AddImportedPreset(() => PresetArchive.Import(dialog.FileName), owner);
    }

    public void ImportPresetFolder(Window? owner)
    {
        var dialog = new OpenFolderDialog { Title = "Папка с .md файлами" };
        if (!ShowDialog(dialog, owner))
        {
            return;
        }

        AddImportedPreset(() => PresetFolderImporter.Import(dialog.FolderName), owner);
    }

    public void ExportPreset(Preset preset, Window? owner)
    {
        var dialog = new SaveFileDialog
        {
            Filter = PresetArchive.DialogFilter,
            Title = "Экспорт пресета",
            FileName = StateStore.SanitizeFileName(preset.Name) + PresetArchive.FileExtension,
            DefaultExt = PresetArchive.FileExtension,
        };

        if (!ShowDialog(dialog, owner))
        {
            return;
        }

        try
        {
            PresetArchive.Export(preset, dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError(owner, $"Не удалось экспортировать пресет:\n{ex.Message}");
        }
    }

    public void Save()
    {
        try
        {
            StateStore.Save(_state);
        }
        catch (Exception ex)
        {
            _tray.ShowWarning("Ошибка сохранения", ex.Message);
        }
    }

    public void Exit()
    {
        _editor?.Close();
        _settingsWindow?.Close();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _saveTimer.Stop();
        Save();
        _hotkeys.Dispose();
        _tray.Dispose();
        _overlay.Close();
        _tabStrip.Close();
    }

    private void ChangePage(int delta)
    {
        var count = _state.ActivePreset.Pages.Count;
        if (count == 0)
        {
            return;
        }

        Settings.ActivePageIndex = ((Settings.ActivePageIndex + delta) % count + count) % count;
        ApplyActiveChange();
    }

    private void Zoom(double delta)
    {
        Settings.Scale = Math.Round(Math.Clamp(Settings.Scale + delta, 0.5, 3.0), 2);
        Refresh();
        ScheduleSave();
    }

    private void ApplyActiveChange()
    {
        Refresh();
        ScheduleSave();
        ActiveChanged?.Invoke();
    }

    private void AddImportedPreset(Func<Preset> import, Window? owner)
    {
        try
        {
            var preset = import();
            preset.Name = MakeUniqueName(preset.Name);
            _state.Presets.Add(preset);
            SelectPreset(_state.Presets.Count - 1);
            Save();
        }
        catch (Exception ex)
        {
            ShowError(owner, $"Не удалось импортировать пресет:\n{ex.Message}");
        }
    }

    private void ApplySettingsReplacement(Window? owner)
    {
        var failed = RegisterHotkeys();
        Refresh();
        Save();
        SettingsReplaced?.Invoke();

        if (failed.Count > 0)
        {
            ShowError(owner, $"Не удалось зарегистрировать горячие клавиши:\n{string.Join(", ", failed)}");
        }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private string MakeUniqueName(string name)
    {
        var candidate = name;
        var counter = 2;
        while (_state.Presets.Any(preset => preset.Name == candidate))
        {
            candidate = $"{name} ({counter++})";
        }

        return candidate;
    }

    private static bool ShowDialog(CommonDialog dialog, Window? owner) =>
        (owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;

    private static void ShowError(Window? owner, string message)
    {
        if (owner != null)
        {
            MessageBox.Show(owner, message, "Focus Overlay", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            MessageBox.Show(message, "Focus Overlay", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
