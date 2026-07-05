using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class DirectivePanelItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string type = string.Empty;

    [ObservableProperty]
    private string content = string.Empty;

    [ObservableProperty]
    private string ttlLabel = string.Empty;

    [ObservableProperty]
    private bool hasTTL;
}

public sealed partial class DirectivesPanelViewModel : ObservableObject
{
    public ObservableCollection<DirectivePanelItemViewModel> Directives { get; } = [];

    public void Clear()
    {
        Directives.Clear();
    }
}
