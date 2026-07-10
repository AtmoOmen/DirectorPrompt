using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class ModelSettingViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string ID { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProviderID { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ModelName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial float Temperature { get; set; } = 0.8f;

    [ObservableProperty]
    public partial string ReasoningEffort { get; set; } = "high";

    [ObservableProperty]
    public partial string ExtraParameters { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? PromptID { get; set; }

    [ObservableProperty]
    public partial bool IsFetchingModels { get; set; }

    [ObservableProperty]
    public partial bool IsTestingConnection { get; set; }

    [ObservableProperty]
    public partial string ModelFetchMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConnectionMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool? ConnectionSuccess { get; set; }

    public ObservableCollection<string> AvailableModels { get; } = [];
}
