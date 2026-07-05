using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class CharacterPanelItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string status = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;
}

public sealed partial class CharacterPanelViewModel : ObservableObject
{
    public ObservableCollection<CharacterPanelItemViewModel> Characters { get; } = [];

    public void Clear()
    {
        Characters.Clear();
    }
}
