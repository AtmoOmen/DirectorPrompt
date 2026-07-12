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

public sealed partial class MemoryPanelViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedScene { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedTag { get; set; } = string.Empty;

    public ObservableCollection<string> AvailableScenes { get; } = [];
    public ObservableCollection<string> AvailableTags { get; } = [];

    private readonly List<MemorySceneGroupViewModel> allGroups = [];

    public ObservableCollection<MemorySceneGroupViewModel> Groups { get; } = [];

    private string AllScenesLabel => Localization.Loc.Get("Memory.Panel.AllScenes");
    private string AllTagsLabel => Localization.Loc.Get("Memory.Panel.AllTags");

    public MemoryPanelViewModel()
    {
        SelectedScene = AllScenesLabel;
        SelectedTag   = AllTagsLabel;
    }

    public void Clear()
    {
        allGroups.Clear();
        Groups.Clear();
        AvailableScenes.Clear();
        AvailableTags.Clear();
    }

    public void SetGroups(IEnumerable<MemorySceneGroupViewModel> groups)
    {
        allGroups.Clear();
        allGroups.AddRange(groups);


        var previousScene = SelectedScene;
        AvailableScenes.Clear();
        AvailableScenes.Add(AllScenesLabel);
        foreach (var g in allGroups)
        {
            if (!string.IsNullOrWhiteSpace(g.SceneLabel))
                AvailableScenes.Add(g.SceneLabel);
        }
        SelectedScene = AvailableScenes.Contains(previousScene) ? previousScene : AllScenesLabel;


        var previousTag = SelectedTag;
        AvailableTags.Clear();
        AvailableTags.Add(AllTagsLabel);
        
        var tags = allGroups.SelectMany(g => g.Items)
                            .Where(i => i != null && !string.IsNullOrWhiteSpace(i.TagsDisplay))
                            .SelectMany(i => i.TagsDisplay.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                            .Distinct()
                            .OrderBy(t => t);
        foreach (var t in tags)
        {
            AvailableTags.Add(t);
        }
        SelectedTag = AvailableTags.Contains(previousTag) ? previousTag : AllTagsLabel;

        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedSceneChanged(string value) => ApplyFilter();
    partial void OnSelectedTagChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Groups.Clear();
        var filter = SearchText?.Trim();
        var sceneFilter = SelectedScene;
        var tagFilter = SelectedTag;
        var allScenesVal = AllScenesLabel;
        var allTagsVal = AllTagsLabel;

        foreach (var g in allGroups)
        {
            if (!string.IsNullOrWhiteSpace(sceneFilter) && sceneFilter != allScenesVal && g.SceneLabel != sceneFilter)
                continue;

            var filteredItems = g.Items.Where(i => {
                if (!string.IsNullOrWhiteSpace(filter) && !i.Content.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!string.IsNullOrWhiteSpace(tagFilter) && tagFilter != allTagsVal)
                {
                    var itemTags = i.TagsDisplay.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (!itemTags.Contains(tagFilter, StringComparer.OrdinalIgnoreCase))
                        return false;
                }

                return true;
            }).ToList();

            if (filteredItems.Count == 0)
                continue;

            var newGroup = new MemorySceneGroupViewModel
            {
                SceneLabel = g.SceneLabel,
                IsExpanded = true
            };

            foreach (var item in filteredItems)
                newGroup.Items.Add(item);

            Groups.Add(newGroup);
        }
    }

    public void RemoveItem(MemoryPanelItemViewModel item)
    {

        foreach (var group in allGroups)
        {
            if (group.Items.Remove(item))
            {
                if (group.Items.Count == 0)
                    allGroups.Remove(group);
                break;
            }
        }


        ApplyFilter();
    }
}
