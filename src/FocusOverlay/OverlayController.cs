using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using FocusOverlay.Models;
using FocusOverlay.Properties;
using FocusOverlay.Services;
using FocusOverlay.Views;
using Microsoft.Win32;

namespace FocusOverlay;

public sealed class OverlayController : IDisposable
{
    private const double ZoomStep = 0.1;
    private const double StripGap = 4;

    private readonly AppState _state;
    private readonly OverlayWindow _overlay = new();
    private readonly TabStripWindow _tabStrip = new();
    private readonly HotkeyManager _hotkeys = new();
    private readonly TrayIcon _tray;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private EditorWindow? _editor;
    private SettingsWindow? _settingsWindow;

    public OverlayController(AppState state)
    {
        _state = state;
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

    public bool RestartRequested { get; private set; }

    public void Start()
    {
        var failed = RegisterHotkeys();
        Refresh();

        if (failed.Count > 0)
        {
            _tray.ShowWarning(Strings.ControllerHotkeysBusy, string.Join(", ", failed));
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
            (hotkeys.NextPreset, () => ChangePreset(1), false),
            (hotkeys.PrevPreset, () => ChangePreset(-1), false),
            (hotkeys.MoveTop, () => MoveOverlay(vertical: false), false),
            (hotkeys.MoveBottom, () => MoveOverlay(vertical: true), false),
            (hotkeys.MoveLeft, () => MoveOverlay(horizontal: -1), false),
            (hotkeys.MoveCenter, () => MoveOverlay(horizontal: 0), false),
            (hotkeys.MoveRight, () => MoveOverlay(horizontal: 1), false),
            (hotkeys.ResetUserView, ResetUserSetting, false),
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
        var view = ResolveView(preset, page);
        var zoom = _state.UserSetting.Scale ?? 1;
        var strip = Settings.TabStrip;
        var stripHeight = strip.Visible ? _tabStrip.Apply(Settings, zoom, preset, pageIndex, _state.Presets.Count) : 0;
        var stripCorner = strip.Detached ? strip.Corner : Settings.Corner;
        var stripOffsetY = strip.Detached ? strip.OffsetY : Settings.OffsetY;
        var minOffsetY = stripHeight > 0 && stripCorner == view.Corner
            ? stripOffsetY + stripHeight + StripGap * Settings.Scale * zoom
            : 0;
        _overlay.Apply(view, preset, page, minOffsetY);
        UpdateVisibility();
    }

    private TabView ResolveView(Preset preset, OverlayPage? page) =>
        TabViewSetting.Resolve(
            TabViewSetting.FromSettings(Settings),
            TabViewSetting.FromMarkdown(preset.SystemContent),
            TabViewSetting.FromMarkdown(page?.Content),
            _state.UserSetting);

    public void ResetUserSetting()
    {
        _state.UserSetting = new TabViewSetting();
        Refresh();
        ScheduleSave();
    }

    private void MoveOverlay(bool? vertical = null, int? horizontal = null)
    {
        var preset = _state.ActivePreset;
        var pageIndex = Settings.ActivePageIndex;
        var current = ResolveView(preset, pageIndex < preset.Pages.Count ? preset.Pages[pageIndex] : null).Corner;
        var isBottom = vertical ?? current is OverlayCorner.BottomLeft or OverlayCorner.BottomCenter or OverlayCorner.BottomRight;
        var column = horizontal ?? current switch
        {
            OverlayCorner.TopLeft or OverlayCorner.BottomLeft => -1,
            OverlayCorner.TopRight or OverlayCorner.BottomRight => 1,
            _ => 0,
        };

        _state.UserSetting.Corner = (isBottom, column) switch
        {
            (false, < 0) => OverlayCorner.TopLeft,
            (false, 0) => OverlayCorner.TopCenter,
            (false, _) => OverlayCorner.TopRight,
            (true, < 0) => OverlayCorner.BottomLeft,
            (true, 0) => OverlayCorner.BottomCenter,
            (true, _) => OverlayCorner.BottomRight,
        };
        Refresh();
        ScheduleSave();
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
        var dialog = new OpenFileDialog { Filter = SettingsArchive.DialogFilter, Title = Strings.ControllerImportSettingsTitle };
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
            ShowError(owner, string.Format(Strings.ControllerImportSettingsFailed, ex.Message));
        }
    }

    public void ExportSettings(Window? owner)
    {
        var dialog = new SaveFileDialog
        {
            Filter = SettingsArchive.DialogFilter,
            Title = Strings.ControllerExportSettingsTitle,
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
            ShowError(owner, string.Format(Strings.ControllerExportSettingsFailed, ex.Message));
        }
    }

    public void ExportBackup(Window? owner)
    {
        var dialog = new SaveFileDialog
        {
            Filter = BackupArchive.DialogFilter,
            Title = Strings.ControllerExportBackupTitle,
            FileName = $"focus-overlay-{DateTime.Now:yyyy-MM-dd}{BackupArchive.FileExtension}",
            DefaultExt = BackupArchive.FileExtension,
        };

        if (!ShowDialog(dialog, owner))
        {
            return;
        }

        try
        {
            Save();
            BackupArchive.Export(_state, dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError(owner, string.Format(Strings.ControllerExportBackupFailed, ex.Message));
        }
    }

    public void ImportBackup(Window? owner)
    {
        var dialog = new OpenFileDialog { Filter = BackupArchive.DialogFilter, Title = Strings.ControllerImportBackupTitle };
        if (!ShowDialog(dialog, owner))
        {
            return;
        }

        var answer = Ask(
            owner,
            Strings.ControllerImportBackupQuestion,
            MessageBoxButton.YesNoCancel);

        if (answer is not (MessageBoxResult.Yes or MessageBoxResult.No))
        {
            return;
        }

        try
        {
            var backup = BackupArchive.Read(dialog.FileName);
            SettingsArchive.ApplyJson(backup.SettingsJson, Settings);

            if (answer == MessageBoxResult.Yes)
            {
                var previous = _state.Presets.ToList();
                _state.Presets.Clear();
                foreach (var preset in backup.Presets)
                {
                    _state.Presets.Add(preset);
                }

                foreach (var preset in previous)
                {
                    TryDeleteAssets(preset);
                }

                Settings.ActivePresetIndex = backup.ActivePresetIndex;
                Settings.ActivePageIndex = backup.ActivePageIndex;
            }
            else
            {
                foreach (var preset in backup.Presets)
                {
                    preset.Name = MakeUniqueName(preset.Name);
                    _state.Presets.Add(preset);
                }
            }

            ApplySettingsReplacement(owner);
            ActiveChanged?.Invoke();
        }
        catch (Exception ex)
        {
            ShowError(owner, string.Format(Strings.ControllerImportBackupFailed, ex.Message));
        }
    }

    public void ResetSettings()
    {
        SettingsArchive.ResetToDefaults(Settings);
        ApplySettingsReplacement(_settingsWindow);
    }

    public void ImportPreset(Window? owner)
    {
        var dialog = new OpenFileDialog { Filter = PresetArchive.DialogFilter, Title = Strings.ControllerImportPresetTitle };
        if (!ShowDialog(dialog, owner))
        {
            return;
        }

        AddImportedPreset(() => PresetArchive.Import(dialog.FileName), owner);
    }

    public void ImportPresetFolder(Window? owner)
    {
        var dialog = new OpenFolderDialog { Title = Strings.ControllerImportFolderTitle };
        if (!ShowDialog(dialog, owner))
        {
            return;
        }

        AddImportedPreset(() => PresetFolderImporter.Import(dialog.FolderName), owner);
    }

    public void OpenDataFolder(Preset preset, Window? owner)
    {
        try
        {
            var presetDirectory = Path.GetDirectoryName(StateStore.GetAssetsDirectory(preset))!;
            var presetsDirectory = Path.GetDirectoryName(presetDirectory)!;
            Directory.CreateDirectory(presetsDirectory);
            var arguments = Directory.Exists(presetDirectory) ? $"/select,\"{presetDirectory}\"" : $"\"{presetsDirectory}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError(owner, string.Format(Strings.ControllerOpenDataFailed, ex.Message));
        }
    }

    public void ExportPreset(Preset preset, Window? owner)
    {
        var dialog = new SaveFileDialog
        {
            Filter = PresetArchive.DialogFilter,
            Title = Strings.ControllerExportPresetTitle,
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
            ShowError(owner, string.Format(Strings.ControllerExportPresetFailed, ex.Message));
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
            _tray.ShowWarning(Strings.ControllerSaveError, ex.Message);
        }
    }

    public void Exit()
    {
        _editor?.Close();
        _settingsWindow?.Close();
        Application.Current.Shutdown();
    }

    public void Restart()
    {
        RestartRequested = true;
        Exit();
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

    private void ChangePreset(int delta)
    {
        var count = _state.Presets.Count;
        if (count <= 1)
        {
            return;
        }

        SelectPreset(((Settings.ActivePresetIndex + delta) % count + count) % count);
    }

    private static void TryDeleteAssets(Preset preset)
    {
        try
        {
            StateStore.DeleteAssets(preset);
        }
        catch (Exception)
        {
        }
    }

    private void Zoom(double delta)
    {
        var user = _state.UserSetting;
        var zoom = Math.Round(Math.Clamp((user.Scale ?? 1) + delta, 0.3, 3.0), 2);
        user.Scale = Math.Abs(zoom - 1) < 0.001 ? null : zoom;
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
            ShowImportIssues(preset, owner);
        }
        catch (Exception ex)
        {
            ShowError(owner, string.Format(Strings.ControllerImportPresetFailed, ex.Message));
        }
    }

    private static void ShowImportIssues(Preset preset, Window? owner)
    {
        const int limit = 20;
        var issues = MarkdownDiagnostics.AnalyzeSystem(preset.SystemContent)
            .Select(issue => $"{Preset.SystemFileName} — {issue.Text}")
            .Concat(preset.Pages.SelectMany(page => MarkdownDiagnostics.Analyze(preset, page.Content)
                .Select(issue => string.Format(Strings.ControllerIssueInPage, page.Title, issue.Text))))
            .ToList();

        if (issues.Count == 0)
        {
            return;
        }

        var details = string.Join("\n", issues.Take(limit));
        if (issues.Count > limit)
        {
            details += string.Format(Strings.ControllerIssuesMore, issues.Count - limit);
        }

        ShowMessage(
            owner,
            string.Format(Strings.ControllerImportIssues, preset.Name, details),
            MessageBoxImage.Warning);
    }

    private void ApplySettingsReplacement(Window? owner)
    {
        var failed = RegisterHotkeys();
        Refresh();
        Save();
        SettingsReplaced?.Invoke();

        if (failed.Count > 0)
        {
            ShowError(owner, string.Format(Strings.ControllerRegisterHotkeysFailed, string.Join(", ", failed)));
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

    private static MessageBoxResult Ask(Window? owner, string message, MessageBoxButton buttons) =>
        owner != null
            ? MessageBox.Show(owner, message, "Focus Overlay", buttons, MessageBoxImage.Question)
            : MessageBox.Show(message, "Focus Overlay", buttons, MessageBoxImage.Question);

    private static void ShowError(Window? owner, string message) => ShowMessage(owner, message, MessageBoxImage.Error);

    private static void ShowMessage(Window? owner, string message, MessageBoxImage image)
    {
        if (owner != null)
        {
            MessageBox.Show(owner, message, "Focus Overlay", MessageBoxButton.OK, image);
        }
        else
        {
            MessageBox.Show(message, "Focus Overlay", MessageBoxButton.OK, image);
        }
    }
}
