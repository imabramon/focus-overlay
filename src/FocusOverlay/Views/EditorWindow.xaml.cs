using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusOverlay.Models;
using FocusOverlay.Properties;
using FocusOverlay.Services;
using Microsoft.Win32;

namespace FocusOverlay.Views;

public partial class EditorWindow : Window
{
    private readonly OverlayController _controller;
    private readonly DispatcherTimer _issuesTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private OverlayPage? _page;
    private Preset? _systemPreset;
    private MarkupHelpWindow? _helpWindow;
    private bool _syncing;
    private bool _loading;

    public EditorWindow(OverlayController controller)
    {
        _controller = controller;
        InitializeComponent();

        _issuesTimer.Tick += (_, _) =>
        {
            _issuesTimer.Stop();
            UpdateIssues();
        };

        PresetCombo.ItemsSource = controller.State.Presets;
        SyncSelection();

        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnAnyChange));

        CommandManager.AddPreviewExecutedHandler(ContentBox, OnContentPreviewExecuted);
        ContentBox.AllowDrop = true;
        ContentBox.PreviewDragOver += OnContentDragOver;
        ContentBox.PreviewDrop += OnContentDrop;

        _controller.ActiveChanged += SyncSelection;
        Closed += (_, _) =>
        {
            _controller.ActiveChanged -= SyncSelection;
            _issuesTimer.Stop();
            _helpWindow?.Close();
        };
    }

    private Preset ActivePreset => _controller.State.ActivePreset;

    private void OnAnyChange(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            ScheduleUpdate();
        }
    }

    private void ScheduleUpdate()
    {
        _controller.ScheduleRefresh();
        _issuesTimer.Stop();
        _issuesTimer.Start();
    }

    private bool IsSystemMode => SystemToggle.IsChecked == true;

    private void LoadPage()
    {
        _page = PageList.SelectedItem as OverlayPage;
        var (entries, body) = PageDisplaySettings.Split(_page?.Content);

        _loading = true;
        try
        {
            ContentBox.Text = body;
            PageSettingsForm.Load(entries);
            PageSettingsExpander.IsExpanded = entries.Count > 0;
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadSystem()
    {
        _systemPreset = ActivePreset;
        SystemSettingsForm.Load(PageDisplaySettings.Split(_systemPreset.SystemContent).Entries);
    }

    private void OnContentTextChanged(object sender, TextChangedEventArgs e) => SavePage();

    private void OnPageSettingsChanged(object? sender, EventArgs e)
    {
        SavePage();
        ScheduleUpdate();
    }

    private void SavePage()
    {
        if (!_loading && _page != null)
        {
            _page.Content = PageDisplaySettings.Compose(PageSettingsForm.Entries, ContentBox.Text);
        }
    }

    private void OnSystemSettingsChanged(object? sender, EventArgs e)
    {
        if (_systemPreset == null)
        {
            return;
        }

        _systemPreset.SystemContent = PageDisplaySettings.Compose(SystemSettingsForm.Entries, string.Empty);
        ScheduleUpdate();
    }

    private void UpdateIssues()
    {
        var issues = new List<EditorIssue>();
        if (IsSystemMode)
        {
            issues.AddRange(FormIssues(SystemSettingsForm, null));
        }
        else if (_page != null)
        {
            issues.AddRange(FormIssues(PageSettingsForm, PageSettingsExpander));
            issues.AddRange(MarkdownDiagnostics.Analyze(ActivePreset, ContentBox.Text)
                .Select(issue => new EditorIssue(issue.Text, () => SelectLine(issue.Line))));
        }

        IssueList.ItemsSource = issues;
        IssueCount.Text = issues.Count.ToString();
        IssuesButton.Foreground = issues.Count > 0 ? Brushes.Firebrick : Brushes.Gray;
        NoIssuesText.Visibility = issues.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static IEnumerable<EditorIssue> FormIssues(TabSettingsForm form, Expander? expander) =>
        form.Errors.Select(error => new EditorIssue(error.Value, () =>
        {
            if (expander != null)
            {
                expander.IsExpanded = true;
            }

            form.FocusField(error.Key);
        }));

    private void OnIssuesToggled(object sender, RoutedEventArgs e)
    {
        var open = IssuesButton.IsChecked == true;
        IssuesPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        IssuesColumn.Width = open ? new GridLength(280) : new GridLength(0);
    }

    private void OnIssuesCloseClick(object sender, RoutedEventArgs e) => IssuesButton.IsChecked = false;

    private void OnIssueClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(IssueList, source) is ListBoxItem { DataContext: EditorIssue issue })
        {
            issue.Navigate();
        }
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        if (_helpWindow == null)
        {
            _helpWindow = new MarkupHelpWindow { Owner = this };
            _helpWindow.Closed += (_, _) => _helpWindow = null;
            _helpWindow.Show();
        }

        if (_helpWindow.WindowState == WindowState.Minimized)
        {
            _helpWindow.WindowState = WindowState.Normal;
        }

        _helpWindow.Activate();
    }

    private void SelectLine(int lineIndex)
    {
        var box = ContentBox;
        var text = box.Text;
        var start = 0;
        for (var line = 0; line < lineIndex; line++)
        {
            var next = text.IndexOf('\n', start);
            if (next < 0)
            {
                return;
            }

            start = next + 1;
        }

        var end = text.IndexOf('\n', start);
        var length = (end < 0 ? text.Length : end) - start;
        box.Focus();
        box.Select(start, Math.Max(0, length - (length > 0 && text[start + length - 1] == '\r' ? 1 : 0)));
        box.ScrollToLine(box.GetLineIndexFromCharacterIndex(start));
    }

    private void OnSystemToggled(object sender, RoutedEventArgs e)
    {
        PageEditor.Visibility = IsSystemMode ? Visibility.Collapsed : Visibility.Visible;
        SystemEditor.Visibility = IsSystemMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateIssues();
    }

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

        if (!ReferenceEquals(PageList.SelectedItem, _page))
        {
            LoadPage();
        }

        if (!ReferenceEquals(ActivePreset, _systemPreset))
        {
            LoadSystem();
        }

        UpdateIssues();
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
        if (!ReferenceEquals(PageList.SelectedItem, _page))
        {
            LoadPage();
            UpdateIssues();
        }

        if (!_syncing && PageList.SelectedIndex >= 0)
        {
            SystemToggle.IsChecked = false;
            _controller.SelectPage(PageList.SelectedIndex);
        }
    }

    private void OnNewPresetClick(object sender, RoutedEventArgs e)
    {
        var preset = new Preset { Name = Strings.ModelNewPreset };
        preset.Pages.Add(new OverlayPage { Title = string.Format(Strings.ModelPageNumbered, 1) });
        _controller.State.Presets.Add(preset);
        _controller.SelectPreset(_controller.State.Presets.Count - 1);
    }

    private void OnDeletePresetClick(object sender, RoutedEventArgs e)
    {
        var presets = _controller.State.Presets;
        if (presets.Count <= 1)
        {
            MessageBox.Show(this, Strings.EditorCannotDeleteLastPreset, Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var preset = ActivePreset;
        if (!Confirm(string.Format(Strings.EditorDeletePresetConfirm, preset.Name)))
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

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var menu = ImportButton.ContextMenu!;
        menu.PlacementTarget = ImportButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnImportArchiveClick(object sender, RoutedEventArgs e) => _controller.ImportPreset(this);

    private void OnImportFolderClick(object sender, RoutedEventArgs e) => _controller.ImportPresetFolder(this);

    private void OnExportClick(object sender, RoutedEventArgs e) => _controller.ExportPreset(ActivePreset, this);

    private void OnDataClick(object sender, RoutedEventArgs e) => _controller.OpenDataFolder(ActivePreset, this);

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _controller.OpenSettings();

    private void OnAddPageClick(object sender, RoutedEventArgs e)
    {
        var pages = ActivePreset.Pages;
        pages.Add(new OverlayPage { Title = string.Format(Strings.ModelPageNumbered, pages.Count + 1) });
        _controller.SelectPage(pages.Count - 1);
    }

    private void OnRemovePageClick(object sender, RoutedEventArgs e)
    {
        var pages = ActivePreset.Pages;
        var index = PageList.SelectedIndex;
        if (index < 0 || !Confirm(string.Format(Strings.EditorDeletePageConfirm, pages[index].Title)))
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
            Title = Strings.EditorAddImageTitle,
            Filter = Strings.EditorImageFilter,
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
            MessageBox.Show(this, string.Format(Strings.EditorAddImageFailed, ex.Message), Title, MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(this, string.Format(Strings.EditorPasteImageFailed, ex.Message), Title, MessageBoxButton.OK, MessageBoxImage.Error);
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

    public sealed record EditorIssue(string Text, Action Navigate);
}
