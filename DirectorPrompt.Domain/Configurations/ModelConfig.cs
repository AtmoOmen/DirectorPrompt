namespace DirectorPrompt.Domain.Configurations;

public record ModelConfig
{
    /// <summary>
    ///     "openai" / "anthropic" / "ollama" / "custom"
    /// </summary>
    public string Provider { get; init; } = string.Empty;

    public string Endpoint { get; init; } = string.Empty;

    public string? APIKey { get; init; }

    public string ModelName { get; init; } = string.Empty;

    public string Fingerprint => $"{Provider}|{Endpoint}|{ModelName}";
}
