using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using FocusOverlay.Properties;
using FocusOverlay.Services;

namespace FocusOverlay;

public sealed class TrayIcon : IDisposable
{
    private readonly OverlayController _controller;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _presetsMenu = new(Strings.CommonPreset);

    public TrayIcon(OverlayController controller)
    {
        _controller = controller;

        var menu = new ContextMenuStrip();
        menu.Items.Add(Strings.CommonToggle, null, (_, _) => _controller.Toggle());
        menu.Items.Add(Strings.TrayEditor, null, (_, _) => _controller.OpenEditor());
        menu.Items.Add(Strings.CommonSettings, null, (_, _) => _controller.OpenSettings());
        menu.Items.Add(_presetsMenu);
        menu.Items.Add(Strings.CommonResetUserView, null, (_, _) => _controller.ResetUserSetting());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.TrayImportPreset, null, (_, _) => _controller.ImportPreset(null));
        menu.Items.Add(Strings.TrayImportFolder, null, (_, _) => _controller.ImportPresetFolder(null));
        menu.Items.Add(Strings.TrayExportPreset, null, (_, _) => _controller.ExportPreset(_controller.State.ActivePreset, null));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.TrayExportAll, null, (_, _) => _controller.ExportBackup(null));
        menu.Items.Add(Strings.CommonImportAll, null, (_, _) => _controller.ImportBackup(null));
        menu.Items.Add(Strings.TrayOpenDataFolder, null, (_, _) => OpenDataFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.TrayExit, null, (_, _) => _controller.Exit());
        menu.Opening += (_, _) => RebuildPresetsMenu();

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Focus Overlay",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => _controller.OpenEditor();
    }

    public void ShowWarning(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Warning);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private void RebuildPresetsMenu()
    {
        _presetsMenu.DropDownItems.Clear();
        var presets = _controller.State.Presets;
        for (var i = 0; i < presets.Count; i++)
        {
            var index = i;
            var item = new ToolStripMenuItem(presets[i].Name)
            {
                Checked = i == _controller.Settings.ActivePresetIndex,
            };
            item.Click += (_, _) => _controller.SelectPreset(index);
            _presetsMenu.DropDownItems.Add(item);
        }
    }

    private static void OpenDataFolder()
    {
        System.IO.Directory.CreateDirectory(StateStore.DataDirectory);
        Process.Start(new ProcessStartInfo(StateStore.DataDirectory) { UseShellExecute = true });
    }

    private static Icon LoadIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/eye.ico"));
        using var stream = resource.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
