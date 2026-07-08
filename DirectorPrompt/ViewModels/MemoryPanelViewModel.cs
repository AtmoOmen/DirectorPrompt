using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class MemoryPanelItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial long ID { get; set; }

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TagsDisplay { get; set; } = string.Empty;

    public bool HasTags => !string.IsNullOrWhiteSpace(TagsDisplay);

    [ObservableProperty]
    public partial string SceneLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial long TimelinePos { get; set; }

    [ObservableProperty]
    public partial string RelatedCharacters { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRelatedCharacters { get; set; }

    [ObservableProperty]
    public partial string UpdatedAtDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditingContent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditingTags { get; set; } = string.Empty;

    public void StartEdit()
    {
        EditingContent = Content;
        EditingTags    = TagsDisplay;
        IsEditing      = true;
    }

    public void CancelEdit() =>
        IsEditing = false;

    public void CommitEdit()
    {
        Content    = EditingContent;
        TagsDisplay = EditingTags;
        IsEditing  = false;
    }
}

public sealed class MemoryPanelViewModel : ObservableObject
{
    public ObservableCollection<MemoryPanelItemViewModel> Memories { get; } = [];

    public void Clear() =>
        Memories.Clear();
}
