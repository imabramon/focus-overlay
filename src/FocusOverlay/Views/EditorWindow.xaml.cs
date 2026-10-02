using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FocusOverlay.Models;
using FocusOverlay.Services;
using Microsoft.Win32;

namespace FocusOverlay.Views;

public partial class EditorWindow : Window
{
    private readonly OverlayController _controller;
    private bool _syncing;

    public EditorWindow(OverlayController controller)
    {
        _controller = controller;
        InitializeComponent();

        PresetCombo.ItemsSource = controller.State.Presets;
        SyncSelection();

        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnAnyChange));

        CommandManager.AddPreviewExecutedHandler(ContentBox, OnContentPreviewExecuted);
        ContentBox.AllowDrop = true;
        ContentBox.PreviewDragOver += OnContentDragOver;
        ContentBox.PreviewDrop += OnContentDrop;

        _controller.ActiveChanged += SyncSelection;
        Closed += (_, _) => _controller.ActiveChanged -= SyncSelection;
    }

    private Preset ActivePreset => _controller.State.ActivePreset;

    private void OnAnyChange(object sender, RoutedEventArgs e) => _controller.ScheduleRefresh();

    private void SyncSelection()
    {
        _syncing = true;
        try
        {
            var preset = ActivePreset;
            PresetCombo.SelectedIndex = _controller.Settings.ActivePresetIndex;
            if (!ReferenceEquals(PageList.ItemsSource, preset.Pages))
            {
                PageList.ItemsSource = preset.Pages;
            }

            PageList.SelectedIndex = preset.Pages.Count > 0 ? _controller.Settings.ActivePageIndex : -1;
            PageList.ScrollIntoView(PageList.SelectedItem);
            PageEditor.IsEnabled = PageList.SelectedItem != null;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && PresetCombo.SelectedIndex >= 0)
        {
            _controller.SelectPreset(PresetCombo.SelectedIndex);
        }
    }

    private void OnPageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PageEditor.IsEnabled = PageList.SelectedItem != null;
        if (!_syncing && PageList.SelectedIndex >= 0)
        {
            _controller.SelectPage(PageList.SelectedIndex);
        }
    }

    private void OnNewPresetClick(object sender, RoutedEventArgs e)
    {
        var preset = new Preset { Name = "Новый пресет" };
        preset.Pages.Add(new OverlayPage { Title = "Вкладка 1" });
        _controller.State.Presets.Add(preset);
        _controller.SelectPreset(_controller.State.Presets.Count - 1);
    }

    private void OnDeletePresetClick(object sender, RoutedEventArgs e)
    {
        var presets = _controller.State.Presets;
        if (presets.Count <= 1)
        {
            MessageBox.Show(this, "Нельзя удалить единственный пресет.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var preset = ActivePreset;
        if (!Confirm($"Удалить пресет «{preset.Name}» вместе с его изображениями?"))
        {
            return;
        }

        var index = presets.IndexOf(preset);
        _syncing = true;
        presets.RemoveAt(index);
        _syncing = false;

        try
        {
            StateStore.DeleteAssets(preset);
        }
        catch (Exception)
        {
        }

        _controller.SelectPreset(Math.Min(index, presets.Count - 1));
    }

    private void OnImportClick(object sender, RoutedEventArgs e) => _controller.ImportPreset(this);

    private void OnImportFolderClick(object sender, RoutedEventArgs e) => _controller.ImportPresetFolder(this);

    private void OnExportClick(object sender, RoutedEventArgs e) => _controller.ExportPreset(ActivePreset, this);

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _controller.OpenSettings();

    private void OnAddPageClick(object sender, RoutedEventArgs e)
    {
        var pages = ActivePreset.Pages;
        pages.Add(new OverlayPage { Title = $"Вкладка {pages.Count + 1}" });
        _controller.SelectPage(pages.Count - 1);
    }

    private void OnRemovePageClick(object sender, RoutedEventArgs e)
    {
        var pages = ActivePreset.Pages;
        var index = PageList.SelectedIndex;
        if (index < 0 || !Confirm($"Удалить вкладку «{pages[index].Title}»?"))
        {
            return;
        }

        _syncing = true;
        pages.RemoveAt(index);
        _syncing = false;
        _controller.SelectPage(Math.Max(0, Math.Min(index, pages.Count - 1)));
    }

    private void OnMovePageUpClick(object sender, RoutedEventArgs e) => MovePage(-1);

    private void OnMovePageDownClick(object sender, RoutedEventArgs e) => MovePage(1);

    private void MovePage(int delta)
    {
        var pages = ActivePreset.Pages;
        var index = PageList.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= pages.Count)
        {
            return;
        }

        _syncing = true;
        pages.Move(index, target);
        _syncing = false;
        _controller.SelectPage(target);
    }

    private void OnInsertImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Добавить изображение",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.tif;*.tiff;*.ico|Все файлы (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            InsertImageFiles(dialog.FileNames);
        }
    }

    private void OnContentPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Paste)
        {
            return;
        }

        if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList().Cast<string>().Where(StateStore.IsImageFile).ToList();
            if (files.Count > 0)
            {
                InsertImageFiles(files);
                e.Handled = true;
            }

            return;
        }

        if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource image)
        {
            SaveClipboardImage(image);
            e.Handled = true;
        }
    }

    private void OnContentDragOver(object sender, DragEventArgs e)
    {
        if (GetDroppedImages(e).Count > 0)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnContentDrop(object sender, DragEventArgs e)
    {
        var files = GetDroppedImages(e);
        if (files.Count == 0)
        {
            return;
        }

        var position = ContentBox.GetCharacterIndexFromPoint(e.GetPosition(ContentBox), true);
        if (position >= 0)
        {
            ContentBox.CaretIndex = position;
        }

        InsertImageFiles(files);
        e.Handled = true;
    }

    private static List<string> GetDroppedImages(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(StateStore.IsImageFile).ToList()
            : new List<string>();

    private void InsertImageFiles(IEnumerable<string> files)
    {
        try
        {
            var snippets = files.Select(file =>
            {
                var fileName = StateStore.ImportAsset(ActivePreset, file);
                return $"![{Path.GetFileNameWithoutExtension(fileName)}]({fileName})";
            });
            InsertAtCaret(string.Join(Environment.NewLine, snippets));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось добавить изображение:\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveClipboardImage(BitmapSource image)
    {
        try
        {
            var path = StateStore.CreateAssetPath(ActivePreset, $"image-{DateTime.Now:yyyyMMdd-HHmmss}.png", out var fileName);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            InsertAtCaret($"![]({fileName})");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось вставить изображение:\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InsertAtCaret(string text)
    {
        if (!PageEditor.IsEnabled)
        {
            return;
        }

        ContentBox.Focus();
        ContentBox.SelectedText = text;
        ContentBox.CaretIndex = ContentBox.SelectionStart + text.Length;
        ContentBox.SelectionLength = 0;
    }

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
