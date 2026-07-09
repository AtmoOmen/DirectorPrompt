namespace DirectorPrompt.Domain.Configurations;

public record ProviderConfig
{
    public string ID { get; init; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; init; } = string.Empty;

    public string Provider { get; init; } = "openai";

    public string Endpoint { get; init; } = string.Empty;

    public string? APIKey { get; init; }
}
