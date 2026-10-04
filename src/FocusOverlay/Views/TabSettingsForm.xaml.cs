using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FocusOverlay.Models;
using FocusOverlay.Properties;
using FocusOverlay.Services;

namespace FocusOverlay.Views;

public partial class TabSettingsForm : UserControl
{
    private readonly Dictionary<string, TextBox> _textFields;
    private readonly Dictionary<TextBox, (Brush? Border, object? ToolTip)> _defaults = new();
    private bool _loading;

    public TabSettingsForm()
    {
        InitializeComponent();
        _loading = true;

        CornerCombo.ItemsSource = new[]
        {
            new CornerOption(string.Empty, Strings.EditorInherited),
            new CornerOption(PageDisplaySettings.CornerName(OverlayCorner.TopLeft), Strings.CornerTopLeft),
            new CornerOption(PageDisplaySettings.CornerName(OverlayCorner.TopCenter), Strings.CornerTopCenter),
            new CornerOption(PageDisplaySettings.CornerName(OverlayCorner.TopRight), Strings.CornerTopRight),
            new CornerOption(PageDisplaySettings.CornerName(OverlayCorner.BottomLeft), Strings.CornerBottomLeft),
            new CornerOption(PageDisplaySettings.CornerName(OverlayCorner.BottomCenter), Strings.CornerBottomCenter),
            new CornerOption(PageDisplaySettings.CornerName(OverlayCorner.BottomRight), Strings.CornerBottomRight),
        };
        CornerCombo.SelectedIndex = 0;

        _textFields = new Dictionary<string, TextBox>
        {
            [PageDisplaySettings.KeyOffsetX] = OffsetXBox,
            [PageDisplaySettings.KeyOffsetY] = OffsetYBox,
            [PageDisplaySettings.KeyWidth] = WidthBox,
            [PageDisplaySettings.KeyHeight] = HeightBox,
            [PageDisplaySettings.KeyScale] = ScaleBox,
            [PageDisplaySettings.KeyFontSize] = FontSizeBox,
            [PageDisplaySettings.KeyOpacity] = OpacityBox,
        };

        foreach (var box in _textFields.Values)
        {
            _defaults[box] = (box.BorderBrush, box.ToolTip);
        }

        _loading = false;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<KeyValuePair<string, string>> Entries
    {
        get
        {
            var entries = new List<KeyValuePair<string, string>>
            {
                new(PageDisplaySettings.KeyCorner, CornerCombo.SelectedValue as string ?? string.Empty),
            };
            entries.AddRange(_textFields.Select(field => new KeyValuePair<string, string>(field.Key, field.Value.Text)));
            entries.Add(new(PageDisplaySettings.KeyTextColor, TextColorBox.ColorText ?? string.Empty));
            entries.Add(new(PageDisplaySettings.KeyAccentColor, AccentColorBox.ColorText ?? string.Empty));
            return entries;
        }
    }

    public IReadOnlyList<KeyValuePair<string, string>> Errors => _textFields
        .Where(field => !string.IsNullOrWhiteSpace(field.Value.Text))
        .Select(field => new KeyValuePair<string, string>(field.Key, PageDisplaySettings.ValidateValue(field.Key, field.Value.Text) ?? string.Empty))
        .Where(error => error.Value.Length > 0)
        .ToList();

    public void Load(IReadOnlyDictionary<string, string> entries)
    {
        _loading = true;
        try
        {
            var corner = entries.GetValueOrDefault(PageDisplaySettings.KeyCorner, string.Empty);
            CornerCombo.SelectedValue = corner;
            if (CornerCombo.SelectedIndex < 0)
            {
                CornerCombo.SelectedIndex = 0;
            }

            foreach (var (key, box) in _textFields)
            {
                box.Text = entries.GetValueOrDefault(key, string.Empty);
            }

            TextColorBox.ColorText = entries.GetValueOrDefault(PageDisplaySettings.KeyTextColor, string.Empty);
            AccentColorBox.ColorText = entries.GetValueOrDefault(PageDisplaySettings.KeyAccentColor, string.Empty);
        }
        finally
        {
            _loading = false;
        }

        UpdateErrors();
    }

    public void FocusField(string key)
    {
        if (_textFields.TryGetValue(key, out var box))
        {
            box.Focus();
            box.SelectAll();
        }
    }

    private void OnFieldChanged(object sender, EventArgs e)
    {
        if (e is RoutedEventArgs routed)
        {
            routed.Handled = true;
        }

        if (_loading)
        {
            return;
        }

        UpdateErrors();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnClearTextColorClick(object sender, RoutedEventArgs e) => ClearColor(TextColorBox);

    private void OnClearAccentColorClick(object sender, RoutedEventArgs e) => ClearColor(AccentColorBox);

    private void ClearColor(ColorPickerBox box)
    {
        if (string.IsNullOrEmpty(box.ColorText))
        {
            return;
        }

        box.ColorText = string.Empty;
        OnFieldChanged(box, EventArgs.Empty);
    }

    private void UpdateErrors()
    {
        var errors = Errors.ToDictionary(error => error.Key, error => error.Value);
        foreach (var (key, box) in _textFields)
        {
            var (border, toolTip) = _defaults[box];
            var hasError = errors.TryGetValue(key, out var message);
            box.BorderBrush = hasError ? Brushes.Firebrick : border;
            box.ToolTip = hasError ? message : toolTip;
        }
    }

    public sealed record CornerOption(string Value, string Label);
}
