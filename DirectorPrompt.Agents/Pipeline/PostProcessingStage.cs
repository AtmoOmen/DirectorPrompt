using DirectorPrompt.Agents.Prompts;
using DirectorPrompt.Agents.Tools;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Pipeline;

public sealed class PostProcessingStage
{
    private readonly IChatClientFactory chatClientFactory;
    private readonly MemoryTools        memoryTools;
    private readonly StateTools         stateTools;
    private readonly CharacterTools     characterTools;
    private readonly OrchestratorConfig orchestratorConfig;

    public PostProcessingStage
    (
        IChatClientFactory chatClientFactory,
        MemoryTools        memoryTools,
        StateTools         stateTools,
        CharacterTools     characterTools,
        OrchestratorConfig orchestratorConfig
    )
    {
        this.chatClientFactory  = chatClientFactory;
        this.memoryTools        = memoryTools;
        this.stateTools         = stateTools;
        this.characterTools     = characterTools;
        this.orchestratorConfig = orchestratorConfig;
    }

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var memoryAgent = orchestratorConfig.Agents.FirstOrDefault(a => a.Role == AgentRole.Memory);

        if (memoryAgent is null || !memoryAgent.Enabled)
        {
            Log.Debug("PostProcessingStage: Memory Agent 未启用, 跳过");
            return;
        }

        Log.Information
        (
            "PostProcessingStage 开始: 模型={Model}",
            memoryAgent.ModelConfig.ModelName
        );

        var client      = chatClientFactory.Create(memoryAgent.ModelConfig);
        var toolContext = context.ToolContext;

        var tools = new List<AIFunction>();
        tools.AddRange(memoryTools.Create(toolContext));
        tools.AddRange(stateTools.Create(toolContext));
        tools.AddRange(characterTools.Create(toolContext));

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, MemorySubAgentPrompt.Update),
            new(ChatRole.User, context.NarrativeOutput ?? string.Empty)
        };

        var options = new ChatOptions
        {
            Temperature = memoryAgent.Temperature,
            ModelId     = memoryAgent.ModelConfig.ModelName,
            Tools       = [.. tools]
        };

        await client.GetResponseAsync(messages, options, cancellationToken);

        Log.Information("PostProcessingStage 完成");
    }
}
