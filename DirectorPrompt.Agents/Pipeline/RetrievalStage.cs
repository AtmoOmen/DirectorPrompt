using System.Text;
using DirectorPrompt.Agents.Retrieval;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Serilog;

namespace DirectorPrompt.Agents.Pipeline;

public sealed class RetrievalStage
(
    ISceneRepository          sceneRepository,
    IStateRepository          stateRepository,
    ICharacterRepository      characterRepository,
    IDirectiveRepository      directiveRepository,
    EmbeddingIndexService     embeddingIndexService,
    KnowledgeRetrievalService knowledgeRetrievalService,
    MemoryRetrievalService    memoryRetrievalService
)
{
    public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        Log.Information("RetrievalStage 开始: 对话={SessionID}, 轮次={RoundID}", context.SessionID, context.RoundID);

        var toolContext = context.ToolContext;
        var indexingTask = embeddingIndexService.SynchronizeProjectAsync
        (
            toolContext.ProjectID,
            toolContext.EmbeddingConfig,
            cancellationToken
        );
        var queryTask     = BuildRetrievalQueryAsync(context, cancellationToken);
        var injectionTask = BuildSystemInjectionAsync(toolContext, cancellationToken);

        await Task.WhenAll(indexingTask, queryTask, injectionTask);

        var query         = await queryTask;
        var knowledgeTask = knowledgeRetrievalService.SearchAsync(toolContext, query, cancellationToken);
        var memoryTask    = memoryRetrievalService.SearchAsync(toolContext, query, cancellationToken);

        await Task.WhenAll(knowledgeTask, memoryTask);

        context.KnowledgeContext = FormatKnowledgeContext(await knowledgeTask);
        context.MemoryContext    = FormatMemoryContext(await memoryTask);
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

    private async Task<string> BuildRetrievalQueryAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();

        if (context.CurrentSceneID is not null)
        {
            var sceneTask      = sceneRepository.GetByIDAsync(context.CurrentSceneID.Value, cancellationToken);
            var charactersTask = characterRepository.GetBySceneAsync(context.CurrentSceneID.Value, cancellationToken);

            await Task.WhenAll(sceneTask, charactersTask);

            var scene = await sceneTask;

            if (scene is not null)
            {
                sb.AppendLine("当前场景:");
                sb.AppendLine($"时间: {scene.TimeLabel}");

                if (!string.IsNullOrWhiteSpace(scene.ProgressSummary))
                    sb.AppendLine($"进展: {scene.ProgressSummary}");
                else if (!string.IsNullOrWhiteSpace(scene.Summary))
                    sb.AppendLine($"摘要: {scene.Summary}");
            }

            var characters = await charactersTask;

            if (characters.Count > 0)
            {
                sb.AppendLine("在场人物:");

                foreach (var character in characters)
                {
                    var aliases = character.Aliases.Length > 0 ?
                                      $", 别称: {string.Join("、", character.Aliases)}" :
                                      string.Empty;
                    sb.AppendLine($"- {character.Name}{aliases}: {character.Description}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(context.PreviousSceneSummary))
        {
            sb.AppendLine("上一场景摘要:");
            sb.AppendLine(context.PreviousSceneSummary);
        }

        sb.AppendLine("导演指令:");

        foreach (var item in context.DirectiveBatch.Directives)
            sb.AppendLine($"{item.Order}. [{item.Type}] {item.Content}");

        return sb.ToString();
    }

    private static string FormatKnowledgeContext(IReadOnlyList<KnowledgeRetrievalResult> results)
    {
        var sb = new StringBuilder();

        foreach (var result in results)
        {
            if (!string.IsNullOrWhiteSpace(result.Remarks))
                sb.AppendLine($"### {result.Remarks}");

            sb.AppendLine(result.Content);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatMemoryContext(IReadOnlyList<MemoryRetrievalResult> results)
    {
        var sb = new StringBuilder();

        foreach (var result in results)
        {
            sb.AppendLine(result.Content);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
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
            var attrIDs     = attributes.Select(a => a.ID).ToList();
            var stateValues = await stateRepository.GetStateValuesAsync(attrIDs, context.SessionID, cancellationToken);
            var valueMap    = stateValues.ToDictionary(v => v.AttributeID);

            sb.AppendLine("## 全局状态");

            foreach (var attr in attributes)
            {
                var value = valueMap.TryGetValue(attr.ID, out var sv) ?
                                sv :
                                null;
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

        var attrLookup     = attributes.ToDictionary(a => a.ID);
        var characterIDs   = characters.Select(c => c.ID).ToList();
        var allStateValues = await characterRepository.GetCharacterStateValuesBatchAsync(characterIDs, cancellationToken);
        var valuesByChar = allStateValues
                           .Where(v => attrLookup.ContainsKey(v.AttributeID))
                           .GroupBy(v => v.CharacterID)
                           .ToDictionary(g => g.Key);

        sb.AppendLine("## 在场人物状态");

        foreach (var character in characters)
        {
            sb.AppendLine($"{character.Name}:");

            foreach (var attr in attributes)
            {
                var value = valuesByChar.TryGetValue(character.ID, out var charValues) ?
                                charValues.FirstOrDefault(v => v.AttributeID == attr.ID) :
                                null;
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
        var characterIDs = characters.Select(c => c.ID).ToList();
        var idSet        = characterIDs.ToHashSet();
        var allRelations = await characterRepository.GetRelationsByCharactersAsync(characterIDs, cancellationToken);

        var merged = new Dictionary<(long Source, long Target), CharacterRelation>();

        foreach (var r in allRelations)
        {
            if (idSet.Contains(r.SourceCharacterID) && idSet.Contains(r.TargetCharacterID))
                merged[(r.SourceCharacterID, r.TargetCharacterID)] = r;
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
}
