namespace DirectorPrompt.Domain.Configurations;

public record UserSettings
{
    public UserOrchestratorConfig Orchestrator { get; init; } = new();
    public LocalizationConfig Localization { get; init; } = new();
}
