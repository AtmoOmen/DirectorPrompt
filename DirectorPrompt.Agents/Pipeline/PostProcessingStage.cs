using System.Globalization;
using System.Text;
using System.Text.Json;
using DirectorPrompt.Agents.Config;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Pipeline;

public sealed class PostProcessingStage
(
    IChatClientFactory   chatClientFactory,
    AgentConfigResolver  agentConfigResolver,
    IAgentToolResolver   agentToolResolver,
    IStateRepository     stateRepository,
    ICharacterRepository characterRepository,
    ISceneRepository     sceneRepository
)
{
    private const int ARCHIVE_THRESHOLD = 15;

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

        var client      = chatClientFactory.Create(resolved.ProviderConfig, resolved.ModelConfig);
        var toolContext = context.ToolContext;

        var tools = await agentToolResolver.ResolveAsync
                    (
                        AgentTaskType.MemoryUpdate,
                        toolContext,
                        cancellationToken
                    );

        var agentContext = await BuildAgentContextAsync(toolContext, cancellationToken);

        var userContent = $"{agentContext}\n\n---\n\n## 叙事文本\n{context.NarrativeOutput ?? string.Empty}";

        if (context.NarrativeIntents.Count > 0)
        {
            userContent += "\n\n## 叙事意图\n仅用于理解正文，状态和记忆更新必须以已发生的正文事实为依据，不得将伏笔、计划或未发生事件记录为事实。\n";
            userContent += JsonSerializer.Serialize(context.NarrativeIntents, JsonOptions.Default);
        }

        var messages = DirectiveProcessingStage.BuildMessages(resolved.SystemPrompt, resolved.ModelPrompt, userContent);

        var options = new ChatOptions
        {
            Temperature = resolved.ModelConfig.Temperature,
            ModelId     = resolved.ModelConfig.ModelName,
            Tools       = [.. tools]
        };

        var response   = await client.GetResponseAsync(messages, options, cancellationToken);
        var calls      = new Dictionary<string, string>(StringComparer.Ordinal);
        var toolErrors = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var content in response.Messages.SelectMany(message => message.Contents))
        {
            switch (content)
            {
                case FunctionCallContent call:
                    calls[call.CallId] = call.Name;
                    break;
                case FunctionResultContent result when calls.TryGetValue(result.CallId, out var toolName):
                    if (ToolResult.IsError(result.Result))
                        toolErrors[toolName] = result.Result?.ToString() ?? string.Empty;
                    else
                        toolErrors.Remove(toolName);
                    break;
            }
        }

        if (toolErrors.Count > 0)
            throw new InvalidOperationException($"记忆与状态更新尚未完成：{string.Join("；", toolErrors.Select(error => $"{error.Key}: {error.Value}"))}");

        await characterRepository.ArchiveStaleAsync
        (
            context.SessionID,
            context.RoundID,
            ARCHIVE_THRESHOLD,
            cancellationToken
        );

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

        var attributes = (await stateRepository.GetAttributesAsync(context.ProjectID, StateScope.Global, cancellationToken))
                         .Where(a => a.Driver == Driver.Narrative)
                         .ToList();

        if (attributes.Count > 0)
        {
            var attrIDs     = attributes.Select(a => a.ID).ToList();
            var stateValues = await stateRepository.GetStateValuesAsync(attrIDs, context.SessionID, cancellationToken);
            var valueMap    = stateValues.ToDictionary(v => v.AttributeID);

            sb.AppendLine("## 可用状态属性 (调用工具时使用 Name 字段的值)");
            sb.AppendLine("| Name | 显示名 | 当前值 | 类型 | 约束 | 规则 |");
            sb.AppendLine("|------|--------|--------|------|------|------|");

            foreach (var attr in attributes)
            {
                var value      = valueMap.GetValueOrDefault(attr.ID);
                var type       = FormatType(attr);
                var constraint = FormatConstraint(attr);
                var rules      = FormatRules(attr);

                sb.AppendLine($"| {attr.Name} | {attr.DisplayName} | {value?.Value ?? "未设置"} | {type} | {constraint} | {rules} |");
            }

            sb.AppendLine();
        }

        var characters = await characterRepository.GetActiveBySessionAsync(context.SessionID, cancellationToken);

        if (characters.Count > 0)
        {
            var sceneCharacterIDs = new HashSet<long>();

            if (context.SceneID is not null)
            {
                var presence = await characterRepository.GetPresenceAsync(context.SceneID.Value, cancellationToken);
                sceneCharacterIDs = [.. presence.Select(p => p.CharacterID)];
            }

            sb.AppendLine("## 已有人物 (不要重复添加, 如不确定可调用 search_character 检索)");

            foreach (var c in characters)
            {
                var inScene = sceneCharacterIDs.Contains(c.ID) ?
                                  " [在场]" :
                                  "";

                var aliases = c.Aliases.Length > 0 ?
                                  $" (别称: {string.Join(", ", c.Aliases)})" :
                                  "";

                sb.AppendLine($"- {c.Name}{aliases}{inScene}: {c.Description}");
            }

            sb.AppendLine();
        }

        var categoryAttrs = (await stateRepository.GetAttributesAsync(context.ProjectID, StateScope.Category, cancellationToken))
                            .Where(a => a.Driver == Driver.Narrative)
                            .ToList();

        if (categoryAttrs.Count > 0)
        {
            sb.AppendLine("## 人物状态属性 (调用工具时使用 Name 字段的值)");
            sb.AppendLine("| Name | 显示名 | 类型 | 约束 | 规则 |");
            sb.AppendLine("|------|--------|------|------|------|");

            foreach (var attr in categoryAttrs)
            {
                var type       = FormatType(attr);
                var constraint = FormatConstraint(attr);
                var rules      = FormatRules(attr);

                sb.AppendLine($"| {attr.Name} | {attr.DisplayName} | {type} | {constraint} | {rules} |");
            }

            sb.AppendLine();
        }

        if (characters.Count > 0 && categoryAttrs.Count > 0)
        {
            var categoryAttrIDs = categoryAttrs.Select(a => a.ID).ToHashSet();
            var attrLookup      = categoryAttrs.ToDictionary(a => a.ID);
            var characterIDs    = characters.Select(c => c.ID).ToList();
            var allStateValues  = await characterRepository.GetCharacterStateValuesBatchAsync(characterIDs, cancellationToken);
            var valuesByChar = allStateValues
                               .Where(v => categoryAttrIDs.Contains(v.AttributeID))
                               .GroupBy(v => v.CharacterID)
                               .ToDictionary(g => g.Key);

            sb.AppendLine("## 人物当前状态值");

            foreach (var c in characters)
            {
                if (!valuesByChar.TryGetValue(c.ID, out var charValues))
                    continue;

                var parts = new List<string>();

                foreach (var v in charValues)
                {
                    var name = attrLookup.TryGetValue(v.AttributeID, out var attr) ?
                                   attr.Name :
                                   v.AttributeID.ToString();
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
                sb.AppendLine($"- {cat.Name}: {cat.Description}");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatType(StateAttribute attr) =>
        attr.ValueType switch
        {
            StateValueType.Enum => $"Enum({FormatEnumOptions(attr.Config)})",
            StateValueType.Text => "Text",
            _                   => "Numeric"
        };

    private static string FormatConstraint(StateAttribute attr)
    {
        var config = string.IsNullOrWhiteSpace(attr.Config) ?
                         null :
                         JsonSerializer.Deserialize<StateAttributeConfig>(attr.Config, JsonOptions.Default);

        if (config is null)
            return string.Empty;

        var min = config.Min?.ToString(CultureInfo.InvariantCulture);
        var max = config.Max?.ToString(CultureInfo.InvariantCulture);

        var range = (min, max) switch
        {
            (not null, not null) => $"{min}~{max}",
            (not null, null)     => $"≥{min}",
            (null, not null)     => $"≤{max}",
            _                    => string.Empty
        };

        return (string.IsNullOrEmpty(config.Unit), string.IsNullOrEmpty(range)) switch
        {
            (true, _)     => range,
            (false, true) => config.Unit,
            _             => $"{range} {config.Unit}"
        };
    }

    private static string FormatRules(StateAttribute attr)
    {
        var config = string.IsNullOrWhiteSpace(attr.Config) ?
                         null :
                         JsonSerializer.Deserialize<StateAttributeConfig>(attr.Config, JsonOptions.Default);

        return attr.ValueType switch
        {
            StateValueType.Enum when config?.Transitions is { Count: > 0 } transitions => string.Join
            (
                "；",
                transitions.Where(transition => !string.IsNullOrWhiteSpace(transition.ChangeRules))
                           .Select(transition => $"{transition.Option}: {transition.ChangeRules}")
            ),
            StateValueType.Enum                                                            => string.Empty,
            StateValueType.Text when !string.IsNullOrWhiteSpace(config?.NarrativeGuidance) => config.NarrativeGuidance,
            StateValueType.Text                                                            => string.Empty,
            _ when !string.IsNullOrWhiteSpace(config?.ChangeRules)                         => config.ChangeRules,
            _                                                                              => string.Empty
        };
    }

    private static string FormatEnumOptions(string config)
    {
        var parsed = string.IsNullOrWhiteSpace(config) ?
                         null :
                         JsonSerializer.Deserialize<StateAttributeConfig>(config, JsonOptions.Default);

        return parsed is null || parsed.Options.Count == 0 ?
                   string.Empty :
                   string.Join("/", parsed.Options);
    }
}
