namespace DirectorPrompt.Domain.Configurations;

public sealed class MCPServerConfig
{
    public string ID { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public MCPTransportType Transport { get; set; } = MCPTransportType.Stdio;

    public string Command { get; set; } = string.Empty;

    public List<string> Arguments { get; set; } = [];

    public string WorkingDirectory { get; set; } = string.Empty;

    public Dictionary<string, string> Environment { get; set; } = [];

    public string Endpoint { get; set; } = string.Empty;

    public Dictionary<string, string> Headers { get; set; } = [];
}

public enum MCPTransportType
{
    Stdio,
    StreamableHttp
}
