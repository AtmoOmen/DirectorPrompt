using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class DirectivePanelItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Type { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TTLLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasTTL { get; set; }
}

public sealed class DirectivesPanelViewModel : ObservableObject
{
    public ObservableCollection<DirectivePanelItemViewModel> Directives { get; } = [];

    public void Clear() =>
        Directives.Clear();
}
