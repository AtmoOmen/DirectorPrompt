using System.Text.Json.Serialization;

namespace DirectorPrompt.Domain.Configurations;

public sealed class MCPServerConfig
{
    public string ID { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public MCPTransportType Transport { get; set; } = MCPTransportType.Stdio;

    public string Command { get; set; } = string.Empty;

    [JsonIgnore]
    public List<string> Arguments { get; set; } = [];

    [JsonPropertyName("arguments")]
    public List<string?> StoredArguments
    {
        get => Arguments.Select(_ => (string?)null).ToList();
        set => Arguments = value?.Select(argument => argument ?? string.Empty).ToList() ?? [];
    }

    public string WorkingDirectory { get; set; } = string.Empty;

    [JsonIgnore]
    public Dictionary<string, string> Environment { get; set; } = [];

    [JsonPropertyName("environment")]
    public Dictionary<string, string?> StoredEnvironment
    {
        get => Environment.ToDictionary(pair => pair.Key, _ => (string?)null);
        set => Environment = value?.ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty) ?? [];
    }

    public string Endpoint { get; set; } = string.Empty;

    [JsonIgnore]
    public Dictionary<string, string> Headers { get; set; } = [];

    [JsonPropertyName("headers")]
    public Dictionary<string, string?> StoredHeaders
    {
        get => Headers.ToDictionary(pair => pair.Key, _ => (string?)null);
        set => Headers = value?.ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty) ?? [];
    }
}

public enum MCPTransportType
{
    Stdio,
    StreamableHttp
}
