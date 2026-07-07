using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class KnowledgeSettingViewModel : ObservableObject
{
    [ObservableProperty]
    public partial int SemanticTopK { get; set; } = 8;

    [ObservableProperty]
    public partial int TokenBudget { get; set; } = 2000;

    [ObservableProperty]
    public partial float MinRelevance { get; set; }
}
