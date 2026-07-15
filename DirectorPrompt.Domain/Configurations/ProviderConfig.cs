namespace DirectorPrompt.Domain.Configurations;

public sealed class ProviderConfig
{
    public string ID { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public string Provider { get; set; } = "openai";

    public string Endpoint { get; set; } = string.Empty;

    public string? APIKey { get; set; }

    public string? CustomHeaders { get; set; }

    public static string[] AvailableProviders { get; } = ["openai", "ollama", "anthropic"];
}
