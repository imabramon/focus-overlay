using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FocusOverlay.Views;

public partial class ColorPickerBox : UserControl
{
    public static readonly DependencyProperty ColorTextProperty = DependencyProperty.Register(
        nameof(ColorText),
        typeof(string),
        typeof(ColorPickerBox),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (sender, _) => ((ColorPickerBox)sender).OnColorTextChanged()));

    private static readonly string[] _palette =
    [
        "#FFFFFF", "#F0E6D2", "#C8C8C8", "#808080", "#000000",
        "#E0A040", "#FFD700", "#FF8C00", "#FF4500", "#C0392B",
        "#E91E63", "#9B59B6", "#3498DB", "#1ABC9C", "#2ECC71",
    ];

    private double _hue;
    private double _saturation;
    private double _value = 1;
    private byte _alpha = 255;
    private Color _originalColor;
    private bool _updating;

    public ColorPickerBox()
    {
        InitializeComponent();
        BuildPalette();
        PickerPopup.Opened += (_, _) =>
        {
            _originalColor = CurrentColor;
            OldSwatch.Background = new SolidColorBrush(_originalColor);
            Dispatcher.InvokeAsync(UpdateThumbs, DispatcherPriority.Loaded);
        };
    }

    public event EventHandler? ColorChanged;

    public string ColorText
    {
        get => (string)GetValue(ColorTextProperty);
        set => SetValue(ColorTextProperty, value);
    }

    private Color CurrentColor => FromHsv(_hue, _saturation, _value, _alpha);

    private void OnColorTextChanged()
    {
        if (_updating)
        {
            return;
        }

        if (TryParseColor(ColorText, out var color))
        {
            SetHsvFrom(color);
        }

        _updating = true;
        HexBox.Text = ColorText;
        PopupHexBox.Text = ColorText;
        _updating = false;
        UpdateVisuals();
    }

    private void OnHexTextChanged(object sender, TextChangedEventArgs e)
    {
        e.Handled = true;
        if (_updating || sender is not TextBox box || !TryParseColor(box.Text, out var color))
        {
            return;
        }

        SetHsvFrom(color);
        CommitColor();
    }

    private void OnSvMouseDown(object sender, MouseButtonEventArgs e)
    {
        SvArea.CaptureMouse();
        PickSv(e.GetPosition(SvArea));
    }

    private void OnSvMouseMove(object sender, MouseEventArgs e)
    {
        if (SvArea.IsMouseCaptured)
        {
            PickSv(e.GetPosition(SvArea));
        }
    }

    private void OnHueMouseDown(object sender, MouseButtonEventArgs e)
    {
        HueArea.CaptureMouse();
        PickHue(e.GetPosition(HueArea));
    }

    private void OnHueMouseMove(object sender, MouseEventArgs e)
    {
        if (HueArea.IsMouseCaptured)
        {
            PickHue(e.GetPosition(HueArea));
        }
    }

    private void OnAreaMouseUp(object sender, MouseButtonEventArgs e) => ((UIElement)sender).ReleaseMouseCapture();

    private void OnAreaSizeChanged(object sender, SizeChangedEventArgs e) => UpdateThumbs();

    private void OnOkClick(object sender, RoutedEventArgs e) => SwatchButton.IsChecked = false;

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        SetHsvFrom(_originalColor);
        CommitColor();
        SwatchButton.IsChecked = false;
    }

    private void PickSv(Point point)
    {
        _saturation = Math.Clamp(point.X / Math.Max(1, SvArea.ActualWidth), 0, 1);
        _value = 1 - Math.Clamp(point.Y / Math.Max(1, SvArea.ActualHeight), 0, 1);
        CommitColor();
    }

    private void PickHue(Point point)
    {
        _hue = Math.Clamp(point.X / Math.Max(1, HueArea.ActualWidth), 0, 1) * 360;
        CommitColor();
    }

    private void BuildPalette()
    {
        foreach (var hex in _palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Border
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(0, 0, 4, 4),
                CornerRadius = new CornerRadius(3),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(color),
                Cursor = Cursors.Hand,
                ToolTip = hex,
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                SetHsvFrom(Color.FromArgb(_alpha, color.R, color.G, color.B));
                CommitColor();
            };
            Palette.Children.Add(swatch);
        }
    }

    private void CommitColor()
    {
        var text = FormatColor(CurrentColor);

        _updating = true;
        ColorText = text;
        if (!HexBox.IsKeyboardFocusWithin)
        {
            HexBox.Text = text;
        }

        if (!PopupHexBox.IsKeyboardFocusWithin)
        {
            PopupHexBox.Text = text;
        }

        _updating = false;

        UpdateVisuals();
        ColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateVisuals()
    {
        var brush = new SolidColorBrush(CurrentColor);
        brush.Freeze();

        SwatchFill.Background = brush;
        NewSwatch.Background = brush;
        PreviewHeading.Foreground = brush;
        PreviewText.Foreground = brush;
        HueFill.Fill = new SolidColorBrush(FromHsv(_hue, 1, 1, 255));
        UpdateThumbs();
    }

    private void UpdateThumbs()
    {
        Canvas.SetLeft(SvThumb, _saturation * SvArea.ActualWidth - SvThumb.Width / 2);
        Canvas.SetTop(SvThumb, (1 - _value) * SvArea.ActualHeight - SvThumb.Height / 2);
        SvThumb.Stroke = _value > 0.6 && _saturation < 0.4 ? Brushes.Black : Brushes.White;
        Canvas.SetLeft(HueThumb, _hue / 360 * HueArea.ActualWidth - HueThumb.Width / 2);
    }

    private void SetHsvFrom(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        _alpha = color.A;
        _value = max;
        _saturation = max <= 0 ? 0 : delta / max;

        if (delta <= 0)
        {
            return;
        }

        var hue = max == r
            ? 60 * ((g - b) / delta % 6)
            : max == g
                ? 60 * ((b - r) / delta + 2)
                : 60 * ((r - g) / delta + 4);
        _hue = hue < 0 ? hue + 360 : hue;
    }

    private static Color FromHsv(double hue, double saturation, double value, byte alpha)
    {
        var h = (hue % 360 + 360) % 360 / 60;
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(h % 2 - 1));
        var m = value - chroma;

        var (r, g, b) = h switch
        {
            < 1 => (chroma, x, 0.0),
            < 2 => (x, chroma, 0.0),
            < 3 => (0.0, chroma, x),
            < 4 => (0.0, x, chroma),
            < 5 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };

        return Color.FromArgb(alpha, ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);

    private static string FormatColor(Color color) => color.A == 255
        ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            if (ColorConverter.ConvertFromString(text.Trim()) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (Exception)
        {
        }

        return false;
    }
}
