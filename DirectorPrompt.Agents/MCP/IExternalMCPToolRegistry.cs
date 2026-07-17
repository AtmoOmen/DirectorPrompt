using DirectorPrompt.Domain.Configurations;
using Microsoft.Extensions.AI;

namespace DirectorPrompt.Agents.MCP;

public interface IExternalMCPToolRegistry : IAsyncDisposable
{
    Task<IReadOnlyList<AIFunction>> GetToolsAsync
    (
        MCPServerConfig   config,
        CancellationToken cancellationToken = default
    );

    Task<MCPServerInspection> InspectAsync
    (
        MCPServerConfig   config,
        bool              refresh,
        CancellationToken cancellationToken = default
    );

    Task InvalidateAsync(CancellationToken cancellationToken = default);
}

public sealed record MCPToolInfo
(
    string Name,
    string Description
);

public sealed record MCPServerInspection
(
    bool                       IsAvailable,
    IReadOnlyList<MCPToolInfo> Tools,
    string?                    ErrorMessage
);
