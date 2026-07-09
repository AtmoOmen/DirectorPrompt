using System.Text;
using DirectorPrompt.Agents.Tools;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Pipeline;

public sealed class PostProcessingStage
(
    IChatClientFactory   chatClientFactory,
    AgentConfigResolver  agentConfigResolver,
    MemoryTools          memoryTools,
    StateTools           stateTools,
    CharacterTools       characterTools,
    IStateRepository     stateRepository,
    ICharacterRepository characterRepository,
    ISceneRepository     sceneRepository
)
{
    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var resolved = agentConfigResolver.Resolve(AgentTaskType.MemoryUpdate);

        if (resolved is null)
        {
            Log.Debug("PostProcessingStage: Memory Update Agent 未配置, 跳过");
            return;
        }

        Log.Information
        (
            "PostProcessingStage 开始: 模型={Model}",
            resolved.ModelConfig.ModelName
        );

        Log.Debug("后处理输入 (叙事文本):\n{Narrative}", context.NarrativeOutput ?? "(空)");

        var client      = chatClientFactory.Create(resolved.ProviderConfig, resolved.ModelConfig);
        var toolContext = context.ToolContext;

        var tools = new List<AIFunction>();
        tools.AddRange(memoryTools.Create(toolContext));
        tools.AddRange(stateTools.Create(toolContext));
        tools.AddRange(characterTools.Create(toolContext));

        var agentContext = await BuildAgentContextAsync(toolContext, cancellationToken);

        var userContent = $"{agentContext}\n\n---\n\n## 叙事文本\n{context.NarrativeOutput ?? string.Empty}";

        var messages = DirectiveProcessingStage.BuildMessages(resolved.SystemPrompt, resolved.ModelPrompt, userContent);

        var options = new ChatOptions
        {
            Temperature = resolved.ModelConfig.Temperature,
            ModelId     = resolved.ModelConfig.ModelName,
            Tools       = [.. tools]
        };

        await client.GetResponseAsync(messages, options, cancellationToken);

        Log.Information("PostProcessingStage 完成: Memory Update Agent 已处理叙事文本");
    }

    private async Task<string> BuildAgentContextAsync(ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();

        if (context.SceneID is not null)
        {
            var scene = await sceneRepository.GetByIDAsync(context.SceneID.Value, cancellationToken);

            if (scene is not null)
            {
                sb.AppendLine("## 当前场景");
                sb.AppendLine($"- ID: {scene.ID}");
                sb.AppendLine($"- 时间标签: {scene.TimeLabel}");
                sb.AppendLine($"- 状态: {scene.Status}");
                sb.AppendLine();
            }
        }

        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID, StateScope.Global, cancellationToken);

        if (attributes.Count > 0)
        {
            sb.AppendLine("## 可用状态属性 (调用工具时使用 Name 字段的值)");
            sb.AppendLine("| Name | 显示名 | 当前值 | 类型 |");
            sb.AppendLine("|------|--------|--------|------|");

            foreach (var attr in attributes)
            {
                var value = await stateRepository.GetStateValueAsync(attr.ID, context.SessionID, cancellationToken);
                sb.AppendLine($"| {attr.Name} | {attr.DisplayName} | {value?.Value ?? "未设置"} | {attr.ValueType} |");
            }

            sb.AppendLine();
        }

        var characters = await characterRepository.GetBySessionAsync(context.SessionID, cancellationToken);

        if (characters.Count > 0)
        {
            var sceneCharacterIDs = new HashSet<long>();

            if (context.SceneID is not null)
            {
                var presence = await characterRepository.GetPresenceAsync(context.SceneID.Value, cancellationToken);
                sceneCharacterIDs = presence.Select(p => p.CharacterID).ToHashSet();
            }

            sb.AppendLine("## 已有人物 (不要重复添加)");

            foreach (var c in characters)
            {
                var inScene = sceneCharacterIDs.Contains(c.ID) ?
                                  " [在场]" :
                                  "";
                sb.AppendLine($"- {c.Name} ({c.Status}){inScene}: {c.Description}");
            }

            sb.AppendLine();
        }

        var categoryAttrs = await stateRepository.GetAttributesAsync(context.ProjectID, StateScope.Category, cancellationToken);

        if (categoryAttrs.Count > 0)
        {
            sb.AppendLine("## 人物状态属性 (调用工具时使用 Name 字段的值)");
            sb.AppendLine("| Name | 显示名 | 类型 | 驱动 |");
            sb.AppendLine("|------|--------|------|------|");

            foreach (var attr in categoryAttrs)
            {
                var driver = attr.Driver == Driver.System ?
                                 "system (不可修改)" :
                                 "narrative";
                sb.AppendLine($"| {attr.Name} | {attr.DisplayName} | {attr.ValueType} | {driver} |");
            }

            sb.AppendLine();
        }

        if (characters.Count > 0)
        {
            sb.AppendLine("## 人物当前状态值");

            foreach (var c in characters)
            {
                var values = await characterRepository.GetCharacterStateValuesAsync(c.ID, cancellationToken);

                if (values.Count == 0)
                    continue;

                var parts = new List<string>();

                foreach (var v in values)
                {
                    var attr = categoryAttrs.FirstOrDefault(a => a.ID == v.AttributeID);
                    var name = attr?.Name ?? v.AttributeID.ToString();
                    parts.Add($"{name}={v.Value}");
                }

                sb.AppendLine($"- {c.Name}: {string.Join(", ", parts)}");
            }

            sb.AppendLine();
        }

        var categories = await characterRepository.GetCategoriesAsync(context.ProjectID, cancellationToken);

        if (categories.Count > 0)
        {
            sb.AppendLine("## 可用分类 (add_character 时可选用)");

            foreach (var cat in categories)
                sb.AppendLine($"- ID:{cat.ID} {cat.Name}: {cat.Description}");

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
