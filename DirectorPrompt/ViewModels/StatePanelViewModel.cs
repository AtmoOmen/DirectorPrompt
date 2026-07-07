using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Localization;

namespace DirectorPrompt.ViewModels;

public sealed partial class StateItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Scope { get; set; } = string.Empty;
}

public sealed partial class StatePanelViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string CurrentSceneLabel { get; set; } = Loc.Get("State.Panel.NotStarted");

    public ObservableCollection<StateItemViewModel> StateItems { get; } = [];

    public void Clear()
    {
        StateItems.Clear();
        CurrentSceneLabel = Loc.Get("State.Panel.NotStarted");
    }
}
