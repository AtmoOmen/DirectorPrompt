using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class StateItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string value = string.Empty;

    [ObservableProperty]
    private string scope = string.Empty;
}

public sealed partial class StatePanelViewModel : ObservableObject
{
    [ObservableProperty]
    private string currentSceneLabel = "未开始";

    [ObservableProperty]
    private string timelineLabel = "—";

    public ObservableCollection<StateItemViewModel> StateItems { get; } = [];

    public void Clear()
    {
        StateItems.Clear();
        CurrentSceneLabel = "未开始";
        TimelineLabel = "—";
    }
}
