using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.ViewModels;

public sealed partial class AgentSettingViewModel : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private AgentRole role;

    [ObservableProperty]
    private string provider = "openai";

    [ObservableProperty]
    private string endpoint = string.Empty;

    private string apiKey = string.Empty;

    public string APIKey
    {
        get => apiKey;
        set => SetProperty(ref apiKey, value);
    }

    [ObservableProperty]
    private string modelName = string.Empty;

    [ObservableProperty]
    private float temperature;

    [ObservableProperty]
    private bool enabled = true;

    [ObservableProperty]
    private int? maxRetries;

    [ObservableProperty]
    private bool isTestingConnection;

    [ObservableProperty]
    private string connectionMessage = string.Empty;

    [ObservableProperty]
    private bool? connectionSuccess;

    public string RoleDisplay => Role.GetDescription();

    public static string[] AvailableProviders { get; } = ["openai", "ollama", "custom"];
}
