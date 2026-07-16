namespace DirectorPrompt.Domain.Configurations;

public class UserSettings
{
    public OrchestratorConfig Orchestrator { get; set; } = new();

    public EmbeddingConfig EmbeddingConfig { get; set; } = new();

    public LocalizationConfig Localization { get; set; } = new();

    public SessionStateConfig Session { get; set; } = new();

    public RemoteControlConfig RemoteControl { get; set; } = new();
}
