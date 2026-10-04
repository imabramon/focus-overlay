using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FocusOverlay.Services;

namespace FocusOverlay.Views;

public partial class MarkupHelpWindow : Window
{
    private const string ResourceName = "FocusOverlay.Docs.markup.md";

    private static readonly Lazy<string> _markdown = new(ReadMarkdown);

    private double _renderedWidth;

    public MarkupHelpWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Render();
        Loaded += (_, _) => Render();
    }

    private void Render()
    {
        var width = Math.Max(200, Viewer.ActualWidth - 60);
        if (Math.Abs(width - _renderedWidth) < 1)
        {
            return;
        }

        _renderedWidth = width;
        var theme = new MarkdownTheme(
            14,
            Brushes.Black,
            new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xB2)),
            Brushes.Gray,
            new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0x9C)),
            width,
            _ => null);

        var offset = Viewer.Document != null ? GetScrollOffset() : 0;
        Viewer.Document = MarkdownRenderer.Render(_markdown.Value, theme);
        Dispatcher.InvokeAsync(() => SetScrollOffset(offset));
    }

    private double GetScrollOffset() =>
        FindScrollViewer(Viewer)?.VerticalOffset ?? 0;

    private void SetScrollOffset(double offset) =>
        FindScrollViewer(Viewer)?.ScrollToVerticalOffset(offset);

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static string ReadMarkdown()
    {
        using var stream = typeof(MarkupHelpWindow).Assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
