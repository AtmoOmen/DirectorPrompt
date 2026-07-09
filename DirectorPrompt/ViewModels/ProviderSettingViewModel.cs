using CommunityToolkit.Mvvm.ComponentModel;

namespace DirectorPrompt.ViewModels;

public sealed partial class ProviderSettingViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string ID { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Provider { get; set; } = "openai";

    [ObservableProperty]
    public partial string Endpoint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string APIKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsTestingConnection { get; set; }

    [ObservableProperty]
    public partial string ConnectionMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool? ConnectionSuccess { get; set; }

    public static string[] AvailableProviders { get; } = ["openai", "ollama", "anthropic"];
}
