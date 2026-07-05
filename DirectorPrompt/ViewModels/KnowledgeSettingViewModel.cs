using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class KnowledgeSettingViewModel : ObservableObject
{
    [ObservableProperty]
    private int semanticTopK = 8;

    [ObservableProperty]
    private int tokenBudget = 2000;

    [ObservableProperty]
    private float minRelevance;
}
