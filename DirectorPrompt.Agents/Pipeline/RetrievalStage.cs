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
(
    IChatClientFactory   chatClientFactory,
    ISceneRepository     sceneRepository,
    IStateRepository     stateRepository,
    ICharacterRepository characterRepository,
    IDirectiveRepository directiveRepository,
    IKnowledgeRepository knowledgeRepository,
    IMemoryRepository    memoryRepository,
    KnowledgeTools       knowledgeTools,
    MemoryTools          memoryTools,
    OrchestratorConfig   orchestratorConfig
)
{
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

        if (knowledgeAgent is null)
        {
            Log.Debug("Knowledge Agent 未启用, 跳过知识检索");
            return string.Empty;
        }

        var entries    = await knowledgeRepository.GetActiveEntriesAsync(context.DirectiveBatch.ProjectID, cancellationToken);
        var phaseCount = context.PhaseActivatedEntryIDs?.Count ?? 0;

        if (entries.Count == 0 && phaseCount == 0)
        {
            Log.Information("知识检索: 无知识条目, 跳过 AI 调用");
            return "无可用知识";
        }

        Log.Information
        (
            "知识检索: 模型={Model}, 活跃条目数={Count}, Phase激活条目数={PhaseCount}",
            knowledgeAgent.ModelConfig.ModelName,
            entries.Count,
            phaseCount
        );

        var client        = chatClientFactory.Create(knowledgeAgent.ModelConfig);
        var tools         = knowledgeTools.Create(context.ToolContext);
        var directorInput = BuildDirectorInput(context.DirectiveBatch);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, KnowledgeAgentPrompt.SYSTEM),
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

        if (memoryAgent is null)
        {
            Log.Debug("Memory Agent 未启用, 跳过记忆检索");
            return string.Empty;
        }

        var memories = await memoryRepository.GetBySessionAsync(context.SessionID, context.CurrentTimelinePosition, cancellationToken);

        if (memories.Count == 0)
        {
            Log.Information("记忆检索: 无记忆条目, 跳过 AI 调用");
            return "无可用记忆";
        }

        Log.Information("记忆检索: 模型={Model}, 条目数={Count}", memoryAgent.ModelConfig.ModelName, memories.Count);

        var client        = chatClientFactory.Create(memoryAgent.ModelConfig);
        var tools         = memoryTools.Create(context.ToolContext);
        var directorInput = BuildDirectorInput(context.DirectiveBatch);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, MemorySubAgentPrompt.RECALL),
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
                sb.AppendLine($"- {attr.DisplayName} ({attr.Name}): {value?.Value ?? "未设置"}");
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
            var sceneCharacters = await characterRepository.GetBySceneAsync(context.SceneID.Value, cancellationToken);

            if (sceneCharacters.Count > 0)
            {
                sb.AppendLine("## 在场人物");

                foreach (var character in sceneCharacters)
                    sb.AppendLine($"- {character.Name}: {character.Description}");

                sb.AppendLine();

                await InjectCharacterStateAsync(sb, context, sceneCharacters, cancellationToken);
                await InjectCharacterRelationsAsync(sb, context, sceneCharacters, cancellationToken);
            }
        }

        return sb.ToString();
    }

    private async Task InjectCharacterStateAsync
    (
        StringBuilder            sb,
        ToolExecutionContext     context,
        IReadOnlyList<Character> characters,
        CancellationToken        cancellationToken
    )
    {
        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID, StateScope.Category, cancellationToken);

        if (attributes.Count == 0)
            return;

        sb.AppendLine("## 在场人物状态");

        foreach (var character in characters)
        {
            var values = await characterRepository.GetCharacterStateValuesAsync(character.ID, cancellationToken);

            sb.AppendLine($"{character.Name}:");

            foreach (var attr in attributes)
            {
                var value = values.FirstOrDefault(v => v.AttributeID == attr.ID);
                sb.AppendLine($"- {attr.DisplayName} ({attr.Name}): {value?.Value ?? "未设置"}");
            }
        }

        sb.AppendLine();
    }

    private async Task InjectCharacterRelationsAsync
    (
        StringBuilder            sb,
        ToolExecutionContext     context,
        IReadOnlyList<Character> characters,
        CancellationToken        cancellationToken
    )
    {
        var characterIDs = characters.Select(c => c.ID).ToHashSet();
        var merged       = new Dictionary<(long Source, long Target), CharacterRelation>();

        foreach (var character in characters)
        {
            var relations = await characterRepository.GetRelationsByCharacterAsync(character.ID, cancellationToken);

            foreach (var r in relations)
            {
                if (characterIDs.Contains(r.SourceCharacterID) && characterIDs.Contains(r.TargetCharacterID))
                    merged[(r.SourceCharacterID, r.TargetCharacterID)] = r;
            }
        }

        if (merged.Count == 0)
            return;

        sb.AppendLine("## 人物关系");

        var idToName = characters.ToDictionary(c => c.ID);

        foreach (var r in merged.Values)
        {
            var sourceName = idToName.TryGetValue(r.SourceCharacterID, out var s) ?
                                 s.Name :
                                 $"ID:{r.SourceCharacterID}";
            var targetName = idToName.TryGetValue(r.TargetCharacterID, out var t) ?
                                 t.Name :
                                 $"ID:{r.TargetCharacterID}";

            var desc = string.IsNullOrWhiteSpace(r.Description) ?
                           "" :
                           $" ({r.Description})";
            sb.AppendLine($"{sourceName} → {targetName}: {r.RelationType}{desc}");
        }

        sb.AppendLine();
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
