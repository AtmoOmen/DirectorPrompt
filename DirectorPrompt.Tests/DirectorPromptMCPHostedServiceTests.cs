using DirectorPrompt.MCP;
using ModelContextProtocol.Client;
using System.Diagnostics;

namespace DirectorPrompt.Tests;

public sealed class DirectorPromptMCPHostedServiceTests
{
    [Fact]
    public async Task ServiceExposesAllProjectTools()
    {
        var tools = new MCPProjectTools(null!, null!, null!, null!, null!, null!, null!);
        await using var service = new DirectorPromptMCPHostedService(tools);
        await service.StartAsync(CancellationToken.None);

        try
        {
            Assert.True(service.IsAvailable, service.ErrorMessage);

            await using var client = await McpClient.CreateAsync
            (
                new HttpClientTransport
                (
                    new HttpClientTransportOptions
                    {
                        Name = "test-client",
                        Endpoint = new Uri(service.Endpoint),
                        TransportMode = HttpTransportMode.StreamableHttp
                    }
                )
            );
            var toolNames = (await client.ListToolsAsync()).Select(tool => tool.Name).Order().ToArray();

            Assert.Equal
            (
                [
                    "create_project",
                    "delete_project",
                    "export_project",
                    "get_project",
                    "import_project",
                    "list_projects",
                    "manage_character_category",
                    "manage_knowledge_entry",
                    "manage_knowledge_group",
                    "manage_state_attribute",
                    "update_project"
                ],
                toolNames
            );

            var stopStarted = Stopwatch.GetTimestamp();
            await service.StopAsync(CancellationToken.None);

            Assert.True(Stopwatch.GetElapsedTime(stopStarted) < TimeSpan.FromSeconds(5));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
