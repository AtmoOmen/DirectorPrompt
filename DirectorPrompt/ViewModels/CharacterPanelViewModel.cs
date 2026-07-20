using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Localization;

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

    [ObservableProperty]
    public partial string ParentCategoriesText { get; set; } = string.Empty;

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

        foreach (var item in AvailableParentCategories)
            item.PropertyChanged -= OnSelectionItemPropertyChanged;

        AvailableParentCategories.Clear();

        foreach (var cat in allCategories.Where(c => c.ID != ID))
        {
            var item = new CategorySelectionItem
            {
                ID         = cat.ID,
                Name       = cat.Name,
                IsSelected = currentSelections.Contains(cat.ID)
            };
            item.PropertyChanged += OnSelectionItemPropertyChanged;
            AvailableParentCategories.Add(item);
        }

        UpdateParentCategoriesText();
    }

    private void OnSelectionItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CategorySelectionItem.IsSelected))
        {
            UpdateParentCategoriesText();
        }
    }

    private void UpdateParentCategoriesText()
    {
        var selected = AvailableParentCategories.Where(i => i.IsSelected).Select(i => i.Name).ToList();
        ParentCategoriesText = selected.Count > 0 ? string.Join(", ", selected) : string.Empty;
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
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial int ItemCount { get; set; }

    public ObservableCollection<CharacterPanelItemViewModel> Items { get; } = [];

    public CharacterCategoryGroupViewModel() =>
        Items.CollectionChanged += (_, _) => ItemCount = Items.Count;
}

public sealed partial class CharacterPanelViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedCategory { get; set; } = string.Empty;

    public ObservableCollection<string> AvailableCategories { get; } = [];

    private readonly List<CharacterCategoryGroupViewModel> allGroups = [];

    public ObservableCollection<CharacterCategoryGroupViewModel> Groups { get; } = [];

    private string AllCategoriesLabel => Loc.Get("Character.Panel.AllCategories");

    public CharacterPanelViewModel() =>
        SelectedCategory = AllCategoriesLabel;

    public void Clear()
    {
        allGroups.Clear();
        Groups.Clear();
        AvailableCategories.Clear();
    }

    public void SetGroups(IEnumerable<CharacterCategoryGroupViewModel> groups)
    {
        allGroups.Clear();
        allGroups.AddRange(groups);


        var previousCategory = SelectedCategory;
        SelectedCategory = string.Empty;
        AvailableCategories.Clear();
        AvailableCategories.Add(AllCategoriesLabel);

        foreach (var g in allGroups)
        {
            if (!string.IsNullOrWhiteSpace(g.CategoryName))
                AvailableCategories.Add(g.CategoryName);
        }

        SelectedCategory = AvailableCategories.Contains(previousCategory) ?
                               previousCategory :
                               AllCategoriesLabel;

        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Groups.Clear();
        var filter           = SearchText?.Trim();
        var categoryFilter   = SelectedCategory;
        var allCategoriesVal = AllCategoriesLabel;

        foreach (var g in allGroups)
        {

            if (!string.IsNullOrWhiteSpace(categoryFilter) && categoryFilter != allCategoriesVal && g.CategoryName != categoryFilter)
                continue;

            var filteredItems = g.Items.Where
            (i =>
                 string.IsNullOrWhiteSpace(filter)                                  ||
                 i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)        ||
                 i.Description.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                 i.Categories.Contains(filter, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (filteredItems.Count == 0)
                continue;

            var newGroup = new CharacterCategoryGroupViewModel
            {
                CategoryName = g.CategoryName
            };

            foreach (var item in filteredItems)
                newGroup.Items.Add(item);

            Groups.Add(newGroup);
        }
    }
}
