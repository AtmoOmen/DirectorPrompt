using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.ViewModels;

public sealed partial class CharacterStateValueViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;
}

public sealed partial class CharacterRelationViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Target { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Type { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Direction { get; set; } = string.Empty;
}

public sealed partial class CharacterPanelItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial long ID { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Categories { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public bool HasStateValues => StateValues.Count > 0;

    public bool HasRelations => Relations.Count > 0;

    public ObservableCollection<CharacterStateValueViewModel> StateValues { get; } = [];

    public ObservableCollection<CharacterRelationViewModel> Relations { get; } = [];
}

public sealed partial class CategorySelectionItem : ObservableObject
{
    public required long ID { get; init; }

    public required string Name { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public sealed partial class CharacterCategoryEditViewModel : ObservableObject
{
    [ObservableProperty]
    public partial long ID { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Description { get; set; }

    public ObservableCollection<CategorySelectionItem> AvailableParentCategories { get; } = [];

    public ObservableCollection<StateAttributeEditViewModel> StateAttributes { get; } = [];

    public bool HasStateAttributes => StateAttributes.Count > 0;

    private HashSet<long> pendingParentCategoryIDs = [];

    public void SyncFromModel(CharacterCategory category)
    {
        ID          = category.ID;
        Name        = category.Name;
        Description = category.Description;

        pendingParentCategoryIDs = [..category.ParentCategoryIDs];
    }

    public void PopulateAvailableParentCategories(IEnumerable<CharacterCategoryEditViewModel> allCategories)
    {
        var currentSelections = AvailableParentCategories
                                .Where(i => i.IsSelected)
                                .Select(i => i.ID)
                                .ToHashSet();

        foreach (var id in pendingParentCategoryIDs)
            currentSelections.Add(id);

        AvailableParentCategories.Clear();

        foreach (var cat in allCategories.Where(c => c.ID != ID))
        {
            var item = new CategorySelectionItem
            {
                ID         = cat.ID,
                Name       = cat.Name,
                IsSelected = currentSelections.Contains(cat.ID)
            };

            AvailableParentCategories.Add(item);
        }
    }

    public CharacterCategory ToModel(long projectID)
    {
        var parentIDs = AvailableParentCategories.Count > 0 ?
                            AvailableParentCategories.Where(i => i.IsSelected).Select(i => i.ID).ToArray() :
                            pendingParentCategoryIDs.ToArray();

        return new CharacterCategory
        {
            ID                = ID,
            ProjectID         = projectID,
            Name              = Name,
            Description       = Description,
            ParentCategoryIDs = parentIDs
        };
    }
}

public sealed partial class CharacterCategoryGroupViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string CategoryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    [ObservableProperty]
    public partial int ItemCount { get; set; }

    public ObservableCollection<CharacterPanelItemViewModel> Items { get; } = [];

    public CharacterCategoryGroupViewModel() =>
        Items.CollectionChanged += (_, _) => ItemCount = Items.Count;
}

public sealed class CharacterPanelViewModel : ObservableObject
{
    public ObservableCollection<CharacterCategoryGroupViewModel> Groups { get; } = [];

    public void Clear() =>
        Groups.Clear();
}
