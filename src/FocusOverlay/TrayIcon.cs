using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using FocusOverlay.Services;

namespace FocusOverlay;

public sealed class TrayIcon : IDisposable
{
    private readonly OverlayController _controller;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _presetsMenu = new("Пресет");

    public TrayIcon(OverlayController controller)
    {
        _controller = controller;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Показать / скрыть", null, (_, _) => _controller.Toggle());
        menu.Items.Add("Редактор…", null, (_, _) => _controller.OpenEditor());
        menu.Items.Add("Настройки…", null, (_, _) => _controller.OpenSettings());
        menu.Items.Add(_presetsMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Импорт пресета…", null, (_, _) => _controller.ImportPreset(null));
        menu.Items.Add("Импорт папки с .md…", null, (_, _) => _controller.ImportPresetFolder(null));
        menu.Items.Add("Экспорт текущего пресета…", null, (_, _) => _controller.ExportPreset(_controller.State.ActivePreset, null));
        menu.Items.Add("Открыть папку данных", null, (_, _) => OpenDataFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => _controller.Exit());
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
