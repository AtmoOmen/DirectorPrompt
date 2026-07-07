using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class MemorySettingViewModel : ObservableObject
{
    [ObservableProperty]
    public partial int RecallTopK { get; set; } = 10;

    [ObservableProperty]
    public partial int TokenBudget { get; set; } = 1500;

    [ObservableProperty]
    public partial float MinRelevance { get; set; }

    [ObservableProperty]
    public partial float TimeDecayLambda { get; set; }
}
