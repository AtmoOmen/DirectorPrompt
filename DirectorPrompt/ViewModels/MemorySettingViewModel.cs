using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class MemorySettingViewModel : ObservableObject
{
    [ObservableProperty]
    private int recallTopK = 10;

    [ObservableProperty]
    private int tokenBudget = 1500;

    [ObservableProperty]
    private float minRelevance;

    [ObservableProperty]
    private float timeDecayLambda;
}
