using System.Text;
using DirectorPrompt.Agents.Prompts;
using DirectorPrompt.Agents.Tools;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Pipeline;

public sealed class RetrievalStage
{
    private readonly IChatClientFactory   chatClientFactory;
    private readonly ISceneRepository     sceneRepository;
    private readonly IStateRepository     stateRepository;
    private readonly ICharacterRepository characterRepository;
    private readonly IDirectiveRepository directiveRepository;
    private readonly KnowledgeTools       knowledgeTools;
    private readonly MemoryTools          memoryTools;
    private readonly OrchestratorConfig   orchestratorConfig;

    public RetrievalStage
    (
        IChatClientFactory   chatClientFactory,
        ISceneRepository     sceneRepository,
        IStateRepository     stateRepository,
        ICharacterRepository characterRepository,
        IDirectiveRepository directiveRepository,
        KnowledgeTools       knowledgeTools,
        MemoryTools          memoryTools,
        OrchestratorConfig   orchestratorConfig
    )
    {
        this.chatClientFactory   = chatClientFactory;
        this.sceneRepository     = sceneRepository;
        this.stateRepository     = stateRepository;
        this.characterRepository = characterRepository;
        this.directiveRepository = directiveRepository;
        this.knowledgeTools      = knowledgeTools;
        this.memoryTools         = memoryTools;
        this.orchestratorConfig  = orchestratorConfig;
    }

    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        Log.Information("RetrievalStage 开始: 对话={SessionID}, 轮次={RoundID}", context.SessionID, context.RoundID);

        var toolContext   = context.ToolContext;
        var knowledgeTask = RetrieveKnowledgeAsync(context, cancellationToken);
        var memoryTask    = RetrieveMemoryAsync(context, cancellationToken);
        var injectionTask = BuildSystemInjectionAsync(toolContext, cancellationToken);

        await Task.WhenAll(knowledgeTask, memoryTask, injectionTask);

        context.KnowledgeContext = await knowledgeTask;
        context.MemoryContext    = await memoryTask;
        context.SystemInjection  = await injectionTask;

        Log.Information
        (
            "RetrievalStage 完成: 知识上下文长度={KnowledgeLen}, 记忆上下文长度={MemoryLen}, 系统注入长度={InjectionLen}",
            context.KnowledgeContext?.Length ?? 0,
            context.MemoryContext?.Length    ?? 0,
            context.SystemInjection?.Length  ?? 0
        );

        if (!string.IsNullOrWhiteSpace(context.KnowledgeContext))
            Log.Debug("知识上下文内容:\n{Content}", context.KnowledgeContext);

        if (!string.IsNullOrWhiteSpace(context.MemoryContext))
            Log.Debug("记忆上下文内容:\n{Content}", context.MemoryContext);

        if (!string.IsNullOrWhiteSpace(context.SystemInjection))
            Log.Debug("系统注入内容:\n{Content}", context.SystemInjection);
    }

    private async Task<string> RetrieveKnowledgeAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        var knowledgeAgent = orchestratorConfig.Agents.FirstOrDefault(a => a.Role == AgentRole.Knowledge);

        if (knowledgeAgent is null || !knowledgeAgent.Enabled)
        {
            Log.Debug("Knowledge Agent 未启用, 跳过知识检索");
            return string.Empty;
        }

        Log.Information("知识检索: 模型={Model}", knowledgeAgent.ModelConfig.ModelName);

        var client        = chatClientFactory.Create(knowledgeAgent.ModelConfig);
        var tools         = knowledgeTools.Create(context.ToolContext);
        var directorInput = BuildDirectorInput(context.DirectiveBatch);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, KnowledgeAgentPrompt.System),
            new(ChatRole.User, directorInput)
        };

        var options = new ChatOptions
        {
            Temperature = knowledgeAgent.Temperature,
            ModelId     = knowledgeAgent.ModelConfig.ModelName,
            Tools       = [.. tools]
        };

        var response = await client.GetResponseAsync(messages, options, cancellationToken);

        var assistantMessage = response.Messages.LastOrDefault();
        var text             = assistantMessage?.Text ?? string.Empty;

        Log.Information("知识检索完成: 返回长度={Length}", text.Length);

        if (!string.IsNullOrWhiteSpace(text))
            Log.Debug("知识检索结果:\n{Content}", text);

        return text;
    }

    private async Task<string> RetrieveMemoryAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        var memoryAgent = orchestratorConfig.Agents.FirstOrDefault(a => a.Role == AgentRole.Memory);

        if (memoryAgent is null || !memoryAgent.Enabled)
        {
            Log.Debug("Memory Agent 未启用, 跳过记忆检索");
            return string.Empty;
        }

        Log.Information("记忆检索: 模型={Model}", memoryAgent.ModelConfig.ModelName);

        var client        = chatClientFactory.Create(memoryAgent.ModelConfig);
        var tools         = memoryTools.Create(context.ToolContext);
        var directorInput = BuildDirectorInput(context.DirectiveBatch);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, MemorySubAgentPrompt.Recall),
            new(ChatRole.User, directorInput)
        };

        var options = new ChatOptions
        {
            Temperature = memoryAgent.Temperature,
            ModelId     = memoryAgent.ModelConfig.ModelName,
            Tools       = [.. tools]
        };

        var response = await client.GetResponseAsync(messages, options, cancellationToken);

        var assistantMessage = response.Messages.LastOrDefault();
        var text             = assistantMessage?.Text ?? string.Empty;

        Log.Information("记忆检索完成: 返回长度={Length}", text.Length);

        if (!string.IsNullOrWhiteSpace(text))
            Log.Debug("记忆检索结果:\n{Content}", text);

        return text;
    }

    private async Task<string> BuildSystemInjectionAsync(ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();

        var sceneTask = context.SceneID is not null ?
                            sceneRepository.GetByIDAsync(context.SceneID.Value, cancellationToken) :
                            Task.FromResult<Scene?>(null);

        var stateTask      = stateRepository.GetAttributesAsync(context.ProjectID, StateScope.Global, cancellationToken);
        var directivesTask = directiveRepository.GetActiveAsync(context.SessionID, cancellationToken);

        await Task.WhenAll(sceneTask, stateTask, directivesTask);

        var scene = await sceneTask;

        if (scene is not null)
        {
            sb.AppendLine("## 场景信息");
            sb.AppendLine($"时间标签: {scene.TimeLabel}");
            sb.AppendLine($"状态: {scene.Status}");
            sb.AppendLine();
        }

        var attributes = await stateTask;

        if (attributes.Count > 0)
        {
            sb.AppendLine("## 全局状态");

            foreach (var attr in attributes)
            {
                var value = await stateRepository.GetStateValueAsync(attr.ID, context.SessionID, cancellationToken);
                sb.AppendLine($"- {attr.DisplayName}: {value?.Value ?? "未设置"}");
            }

            sb.AppendLine();
        }

        var directives = await directivesTask;

        if (directives.Count > 0)
        {
            sb.AppendLine("## 生效指令");

            foreach (var directive in directives)
            {
                var ttl = directive.TTL.HasValue ?
                              $" (剩余 {directive.TTL} 轮)" :
                              " (永久)";
                sb.AppendLine($"- [{directive.Type}]{directive.Content}{ttl}");
            }

            sb.AppendLine();
        }

        if (context.SceneID is not null)
        {
            var presence = await characterRepository.GetPresenceAsync(context.SceneID.Value, cancellationToken);

            if (presence.Count > 0)
            {
                sb.AppendLine("## 在场人物");

                foreach (var p in presence)
                {
                    var character = await characterRepository.GetByIDAsync(p.CharacterID, cancellationToken);
                    if (character is not null)
                        sb.AppendLine($"- {character.Name}: {character.Description}");
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string BuildDirectorInput(DirectiveBatch batch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 导演指令");
        foreach (var item in batch.Directives)
            sb.AppendLine($"{item.Order}. [{item.Type}] {item.Content}");
        return sb.ToString();
    }
}
