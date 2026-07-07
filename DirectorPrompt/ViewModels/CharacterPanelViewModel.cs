using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

public sealed partial class CharacterCategoryEditViewModel : ObservableObject
{
    [ObservableProperty]
    public partial long ID { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial string ParentCategoryIDsText { get; set; } = string.Empty;

    public ObservableCollection<StateAttributeEditViewModel> StateAttributes { get; } = [];

    public bool HasStateAttributes => StateAttributes.Count > 0;

    public void SyncFromModel(CharacterCategory category)
    {
        ID   = category.ID;
        Name = category.Name;
        Description = category.Description;
        ParentCategoryIDsText = string.Join(", ", category.ParentCategoryIDs);
    }

    public CharacterCategory ToModel(long projectID)
    {
        var parentIDs = ParentCategoryIDsText
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(s => long.TryParse(s, out var id) ? id : 0)
                        .Where(id => id > 0)
                        .ToArray();

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

public sealed partial class CharacterPanelViewModel : ObservableObject
{
    public ObservableCollection<CharacterPanelItemViewModel> Characters { get; } = [];

    public void Clear() =>
        Characters.Clear();
}
