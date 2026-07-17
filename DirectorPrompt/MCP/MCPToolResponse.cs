namespace DirectorPrompt.MCP;

public sealed record MCPToolResponse
(
    int      SchemaVersion,
    bool     Success,
    object?  Data,
    string?  Error = null
);
