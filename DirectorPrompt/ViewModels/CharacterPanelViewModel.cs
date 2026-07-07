using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class CharacterPanelItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;
}

public sealed class CharacterPanelViewModel : ObservableObject
{
    public ObservableCollection<CharacterPanelItemViewModel> Characters { get; } = [];

    public void Clear() =>
        Characters.Clear();
}
