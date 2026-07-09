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
        Content     = EditingContent;
        TagsDisplay = EditingTags;
        IsEditing   = false;
    }
}

public sealed partial class MemorySceneGroupViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string SceneLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    [ObservableProperty]
    public partial int ItemCount { get; set; }

    public ObservableCollection<MemoryPanelItemViewModel> Items { get; } = [];

    public MemorySceneGroupViewModel() =>
        Items.CollectionChanged += (_, _) => ItemCount = Items.Count;
}

public sealed class MemoryPanelViewModel : ObservableObject
{
    public ObservableCollection<MemorySceneGroupViewModel> Groups { get; } = [];

    public void Clear() =>
        Groups.Clear();

    public void RemoveItem(MemoryPanelItemViewModel item)
    {
        foreach (var group in Groups)
        {
            if (!group.Items.Remove(item))
                continue;

            if (group.Items.Count == 0)
                Groups.Remove(group);

            break;
        }
    }
}
