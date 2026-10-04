using System;
using System.Collections.ObjectModel;
using FocusOverlay.Properties;

namespace FocusOverlay.Models;

public sealed class OverlayPage : ObservableObject
{
    private string _title = Strings.ModelNewPage;
    private string _content = string.Empty;

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Content
    {
        get => _content;
        set => SetField(ref _content, value);
    }
}

public sealed class Preset : ObservableObject
{
    public const string SystemFileName = "system.md";

    private string _name = Strings.ModelNewPreset;
    private string _systemContent = string.Empty;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string SystemContent
    {
        get => _systemContent;
        set => SetField(ref _systemContent, value ?? string.Empty);
    }

    public ObservableCollection<OverlayPage> Pages { get; set; } = new();
}
