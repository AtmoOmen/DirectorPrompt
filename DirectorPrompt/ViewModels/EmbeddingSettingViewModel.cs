using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class EmbeddingSettingViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Provider { get; set; } = "openai";

    [ObservableProperty]
    public partial string Endpoint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string APIKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ModelName { get; set; } = "text-embedding-v4";

    [ObservableProperty]
    public partial bool IsTestingConnection { get; set; }

    [ObservableProperty]
    public partial string ConnectionMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool? ConnectionSuccess { get; set; }
}
