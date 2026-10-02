using System;
using System.Collections.ObjectModel;

namespace FocusOverlay.Models;

public sealed class OverlayPage : ObservableObject
{
    private string _title = "Новая вкладка";
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
    private string _name = "Новый пресет";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public ObservableCollection<OverlayPage> Pages { get; set; } = new();
}
