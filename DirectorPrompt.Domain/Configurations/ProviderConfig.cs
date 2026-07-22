using System.Text.Json.Serialization;

namespace DirectorPrompt.Domain.Configurations;

public sealed class ProviderConfig
{
    public string ID { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public string Provider { get; set; } = "openai";

    public string Endpoint { get; set; } = string.Empty;

    [JsonIgnore]
    public string? APIKey { get; set; }

    [JsonPropertyName("apiKey")]
    public string? StoredAPIKey
    {
        get => null;
        set => APIKey = value;
    }

    [JsonIgnore]
    public string? CustomHeaders { get; set; }

    [JsonPropertyName("customHeaders")]
    public string? StoredCustomHeaders
    {
        get => null;
        set => CustomHeaders = value;
    }

    public static string[] AvailableProviders { get; } = ["openai", "ollama", "anthropic"];
}
