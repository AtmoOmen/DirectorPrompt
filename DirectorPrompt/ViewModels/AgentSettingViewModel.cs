using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Localization;

namespace DirectorPrompt.ViewModels;

public sealed partial class AgentSettingViewModel : ObservableObject
{
    [ObservableProperty]
    public partial AgentRole Role { get; set; }

    [ObservableProperty]
    public partial string Provider { get; set; } = "openai";

    [ObservableProperty]
    public partial string Endpoint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string APIKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ModelName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial float Temperature { get; set; }

    [ObservableProperty]
    public partial bool IsTestingConnection { get; set; }

    [ObservableProperty]
    public partial string ConnectionMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool? ConnectionSuccess { get; set; }

    public string SystemPrompt { get; set; } = string.Empty;

    public string[] Tools { get; set; } = [];

    public string RoleDisplay => Loc.Get($"Agent.Role.{Role}");

    public static string[] AvailableProviders { get; } = ["openai", "ollama", "custom"];
}
