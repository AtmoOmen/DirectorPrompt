using System.Text.Json;
using System.Text.Json.Serialization;

namespace DirectorPrompt.Domain.Configurations;

public class UserSettings
{
    public static JsonSerializerOptions JSONOptions { get; } = new()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters             = { new JsonStringEnumConverter() }
    };

    public UserOrchestratorConfig Orchestrator { get; set; } = new();

    public LocalizationConfig Localization { get; set; } = new();

    public SessionStateConfig Session { get; set; } = new();
}
