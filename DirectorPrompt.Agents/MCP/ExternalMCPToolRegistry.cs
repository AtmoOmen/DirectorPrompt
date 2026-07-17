using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DirectorPrompt.Domain.Configurations;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using Serilog;

namespace DirectorPrompt.Agents.MCP;

public sealed class ExternalMCPToolRegistry : IExternalMCPToolRegistry
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly Dictionary<string, Connection> connections = [];
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IReadOnlyList<AIFunction>> GetToolsAsync
    (
        MCPServerConfig   config,
        CancellationToken cancellationToken = default
    )
    {
        if (!config.Enabled)
            return [];

        try
        {
            var connection = await GetConnectionAsync(config, false, cancellationToken);

            return connection.Tools
                             .Select
                             (
                                 tool => (AIFunction)new TimeoutMCPFunction
                                 (
                                     tool.WithName(CreateFunctionName(config.ID, tool.Name)),
                                     config.DisplayName
                                 )
                             )
                             .ToList();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Log.Warning(exception, "外部 MCP 服务不可用: {ServerID} ({ServerName})", config.ID, config.DisplayName);
            return [];
        }
    }

    public async Task<MCPServerInspection> InspectAsync
    (
        MCPServerConfig   config,
        bool              refresh,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var connection = await GetConnectionAsync(config, refresh, cancellationToken);
            var names = connection.Tools.Select(tool => tool.Name).ToList();

            return new MCPServerInspection(true, names, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Log.Warning(exception, "外部 MCP 服务检测失败: {ServerID} ({ServerName})", config.ID, config.DisplayName);
            return new MCPServerInspection(false, [], exception.Message);
        }
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        List<Connection> previous;

        await gate.WaitAsync(cancellationToken);

        try
        {
            previous = [.. connections.Values];
            connections.Clear();
        }
        finally
        {
            gate.Release();
        }

        foreach (var connection in previous)
            await connection.Client.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await InvalidateAsync();
        gate.Dispose();
    }

    private async Task<Connection> GetConnectionAsync
    (
        MCPServerConfig   config,
        bool              refresh,
        CancellationToken cancellationToken
    )
    {
        var fingerprint = JsonSerializer.Serialize(config);

        await gate.WaitAsync(cancellationToken);

        try
        {
            if (connections.TryGetValue(config.ID, out var existing) &&
                !refresh &&
                existing.Fingerprint == fingerprint)
                return existing;

            if (connections.Remove(config.ID, out var previous))
                await previous.Client.DisposeAsync();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            McpClient? client = null;

            try
            {
                var transport = CreateTransport(config);
                client = await McpClient.CreateAsync
                         (
                             transport,
                             new McpClientOptions { InitializationTimeout = Timeout },
                             cancellationToken: timeout.Token
                         );
                var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
                var created = new Connection(fingerprint, client, tools.ToList());

                connections[config.ID] = created;
                return created;
            }
            catch
            {
                if (client is not null)
                    await client.DisposeAsync();

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static IClientTransport CreateTransport(MCPServerConfig config) =>
        config.Transport switch
        {
            MCPTransportType.Stdio => new StdioClientTransport
            (
                new StdioClientTransportOptions
                {
                    Name                 = config.DisplayName,
                    Command              = config.Command,
                    Arguments            = config.Arguments,
                    WorkingDirectory     = string.IsNullOrWhiteSpace(config.WorkingDirectory) ? null : config.WorkingDirectory,
                    EnvironmentVariables = config.Environment.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)
                }
            ),
            MCPTransportType.StreamableHttp => new HttpClientTransport
            (
                new HttpClientTransportOptions
                {
                    Name              = config.DisplayName,
                    Endpoint          = new Uri(config.Endpoint, UriKind.Absolute),
                    TransportMode     = HttpTransportMode.StreamableHttp,
                    ConnectionTimeout = Timeout,
                    AdditionalHeaders = config.Headers
                }
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(config.Transport))
        };

    private static string CreateFunctionName(string serverID, string toolName)
    {
        var safeServerID = string.Concat(serverID.Take(8).Where(char.IsLetterOrDigit));
        var safeToolName = string.Concat(toolName.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
        var hash = Convert.ToHexString
                   (
                       SHA256.HashData(Encoding.UTF8.GetBytes($"{serverID}\0{toolName}"))
                   )[..8]
                   .ToLowerInvariant();
        var prefix = $"mcp_{safeServerID}_";
        var availableLength = Math.Max(1, 64 - prefix.Length - hash.Length - 1);
        var normalizedToolName = safeToolName.Length > availableLength ?
                                     safeToolName[..availableLength] :
                                     safeToolName;

        return $"{prefix}{normalizedToolName}_{hash}";
    }

    private sealed record Connection
    (
        string                       Fingerprint,
        McpClient                    Client,
        IReadOnlyList<McpClientTool> Tools
    );

    private sealed class TimeoutMCPFunction
    (
        AIFunction innerFunction,
        string     serverName
    ) : DelegatingAIFunction(innerFunction)
    {
        protected override async ValueTask<object?> InvokeCoreAsync
        (
            AIFunctionArguments arguments,
            CancellationToken   cancellationToken
        )
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            try
            {
                Log.Information
                (
                    "外部 MCP 工具调用: {ServerName}/{ToolName}, 参数={@Arguments}",
                    serverName,
                    Name,
                    arguments
                );
                var result = await InnerFunction.InvokeAsync(arguments, timeout.Token);
                Log.Information
                (
                    "外部 MCP 工具返回: {ServerName}/{ToolName}, 返回={@Result}",
                    serverName,
                    Name,
                    result
                );
                return result;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                Log.Warning("外部 MCP 工具调用超时: {ServerName}, {ToolName}", serverName, Name);
                return $"外部 MCP 工具调用超时: {serverName}/{Name}";
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "外部 MCP 工具调用失败: {ServerName}, {ToolName}", serverName, Name);
                return $"外部 MCP 工具调用失败: {serverName}/{Name}, {exception.Message}";
            }
        }
    }
}
