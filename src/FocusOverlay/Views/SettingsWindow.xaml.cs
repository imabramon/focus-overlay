using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FocusOverlay.Models;
using FocusOverlay.Services;

namespace FocusOverlay.Views;

public partial class SettingsWindow : Window
{
    private readonly OverlayController _controller;
    private readonly Dictionary<RadioButton, OverlayCorner> _cornerButtons;
    private bool _syncing;

    public SettingsWindow(OverlayController controller)
    {
        _controller = controller;
        InitializeComponent();

        _cornerButtons = new Dictionary<RadioButton, OverlayCorner>
        {
            [CornerTopLeft] = OverlayCorner.TopLeft,
            [CornerTopRight] = OverlayCorner.TopRight,
            [CornerBottomLeft] = OverlayCorner.BottomLeft,
            [CornerBottomRight] = OverlayCorner.BottomRight,
        };

        StripCornerCombo.ItemsSource = new[]
        {
            new CornerOption(OverlayCorner.TopLeft, "Левый верхний"),
            new CornerOption(OverlayCorner.TopRight, "Правый верхний"),
            new CornerOption(OverlayCorner.BottomLeft, "Левый нижний"),
            new CornerOption(OverlayCorner.BottomRight, "Правый нижний"),
        };

        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnAnyChange));
        AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>(OnAnyChange));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnAnyChange));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnAnyChange));
        StripCornerCombo.SelectionChanged += OnAnyChange;

        _controller.SettingsReplaced += Rebind;
        Closed += (_, _) => _controller.SettingsReplaced -= Rebind;

        Rebind();
    }

    private void Rebind()
    {
        _syncing = true;
        try
        {
            LayoutGroup.DataContext = null;
            LayoutGroup.DataContext = _controller.Settings;
            StripGroup.DataContext = null;
            StripGroup.DataContext = _controller.Settings.TabStrip;

            foreach (var (button, corner) in _cornerButtons)
            {
                button.IsChecked = corner == _controller.Settings.Corner;
            }

            BuildHotkeyRows();
            HotkeyStatus.Text = string.Empty;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnAnyChange(object sender, RoutedEventArgs e)
    {
        if (!_syncing)
        {
            _controller.ScheduleRefresh();
        }
    }

    private void OnColorChanged(object? sender, EventArgs e)
    {
        if (!_syncing)
        {
            _controller.ScheduleRefresh();
        }
    }

    private void OnCornerChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton button || !_cornerButtons.TryGetValue(button, out var corner))
        {
            return;
        }

        _controller.Settings.Corner = corner;
        _controller.ScheduleRefresh();
    }

    private void OnImportClick(object sender, RoutedEventArgs e) => _controller.ImportSettings(this);

    private void OnExportClick(object sender, RoutedEventArgs e) => _controller.ExportSettings(this);

    private void OnExportAllClick(object sender, RoutedEventArgs e) => _controller.ExportBackup(this);

    private void OnImportAllClick(object sender, RoutedEventArgs e) => _controller.ImportBackup(this);

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, "Сбросить положение, вид и горячие клавиши к значениям по умолчанию?", Title,
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            _controller.ResetSettings();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void BuildHotkeyRows()
    {
        HotkeysGrid.Children.Clear();
        HotkeysGrid.RowDefinitions.Clear();

        var hotkeys = _controller.Settings.Hotkeys;
        var rows = new (string Label, Func<string> Get, Action<string> Set)[]
        {
            ("Показать / скрыть", () => hotkeys.Toggle, value => hotkeys.Toggle = value),
            ("Следующая вкладка", () => hotkeys.NextPage, value => hotkeys.NextPage = value),
            ("Предыдущая вкладка", () => hotkeys.PrevPage, value => hotkeys.PrevPage = value),
            ("Масштаб +", () => hotkeys.ZoomIn, value => hotkeys.ZoomIn = value),
            ("Масштаб −", () => hotkeys.ZoomOut, value => hotkeys.ZoomOut = value),
            ("Прокрутка вверх", () => hotkeys.ScrollUp, value => hotkeys.ScrollUp = value),
            ("Прокрутка вниз", () => hotkeys.ScrollDown, value => hotkeys.ScrollDown = value),
            ("Следующий пресет", () => hotkeys.NextPreset, value => hotkeys.NextPreset = value),
            ("Предыдущий пресет", () => hotkeys.PrevPreset, value => hotkeys.PrevPreset = value),
        };

        foreach (var (label, get, set) in rows)
        {
            var row = HotkeysGrid.RowDefinitions.Count;
            HotkeysGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var caption = new TextBlock
            {
                Text = label,
                Style = (Style)FindResource("FieldLabel"),
            };
            Grid.SetRow(caption, row);
            HotkeysGrid.Children.Add(caption);

            var box = new TextBox
            {
                Text = get(),
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                Style = (Style)FindResource("Field"),
            };
            box.GotKeyboardFocus += (_, _) => _controller.SuspendHotkeys();
            box.LostKeyboardFocus += (_, _) => ApplyHotkeys();
            box.PreviewKeyDown += (_, e) => CaptureHotkey(e, box, get, set);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            HotkeysGrid.Children.Add(box);
        }
    }

    private static void CaptureHotkey(KeyEventArgs e, TextBox box, Func<string> get, Action<string> set)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab)
        {
            return;
        }

        e.Handled = true;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.ImeProcessed)
        {
            return;
        }

        if (key == Key.Escape)
        {
            box.Text = get();
            Keyboard.ClearFocus();
            return;
        }

        if (key == Key.Back && Keyboard.Modifiers == ModifierKeys.None)
        {
            set(string.Empty);
            box.Text = string.Empty;
            return;
        }

        var gesture = HotkeyGesture.Format(Keyboard.Modifiers, key);
        set(gesture);
        box.Text = gesture;
    }

    private void ApplyHotkeys()
    {
        var failed = _controller.RegisterHotkeys();
        HotkeyStatus.Text = failed.Count > 0
            ? $"Не удалось зарегистрировать (заняты другой программой?): {string.Join(", ", failed)}"
            : string.Empty;
        _controller.ScheduleRefresh();
    }

    public sealed record CornerOption(OverlayCorner Value, string Label);
}
