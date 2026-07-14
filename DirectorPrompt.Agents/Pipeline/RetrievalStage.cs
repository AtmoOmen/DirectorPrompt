using System.Text;
using DirectorPrompt.Agents.Retrieval;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Serilog;

namespace DirectorPrompt.Agents.Pipeline;

public sealed class RetrievalStage
(
    IRoundReadSnapshotRepository roundReadSnapshotRepository,
    EmbeddingIndexService        embeddingIndexService,
    KnowledgeRetrievalService    knowledgeRetrievalService,
    MemoryRetrievalService       memoryRetrievalService,
    IEmbeddingServiceFactory     embeddingServiceFactory
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
        var snapshotTask = roundReadSnapshotRepository.GetAsync
        (
            toolContext.ProjectID,
            toolContext.SessionID,
            toolContext.SceneID,
            cancellationToken
        );

        await Task.WhenAll(indexingTask, snapshotTask);

        var snapshot         = await snapshotTask;
        var query            = BuildRetrievalQuery(context, snapshot);
        var embeddingService = embeddingServiceFactory.Create(toolContext.EmbeddingConfig);
        var queryEmbedding   = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        var queryVector      = EmbeddingConversions.FloatsToBytes(queryEmbedding);
        var knowledgeTask    = knowledgeRetrievalService.SearchAsync(toolContext, queryVector, cancellationToken);
        var memoryTask       = memoryRetrievalService.SearchAsync(toolContext, queryVector, cancellationToken);

        await Task.WhenAll(knowledgeTask, memoryTask);

        context.KnowledgeContext = FormatKnowledgeContext(await knowledgeTask);
        context.MemoryContext    = FormatMemoryContext(await memoryTask);
        context.SystemInjection  = BuildSystemInjection(snapshot);

        Log.Information
        (
            "RetrievalStage 完成: 知识上下文长度={KnowledgeLen}, 记忆上下文长度={MemoryLen}, 系统注入长度={InjectionLen}",
            context.KnowledgeContext?.Length ?? 0,
            context.MemoryContext?.Length    ?? 0,
            context.SystemInjection?.Length  ?? 0
        );

    }

    private static string BuildRetrievalQuery(PipelineContext context, RoundReadSnapshot snapshot)
    {
        var sb = new StringBuilder();

        if (snapshot.Scene is not null)
        {
            sb.AppendLine("当前场景:");
            sb.AppendLine($"时间: {snapshot.Scene.TimeLabel}");

            if (!string.IsNullOrWhiteSpace(snapshot.Scene.ProgressSummary))
                sb.AppendLine($"进展: {snapshot.Scene.ProgressSummary}");
            else if (!string.IsNullOrWhiteSpace(snapshot.Scene.Summary))
                sb.AppendLine($"摘要: {snapshot.Scene.Summary}");

            if (snapshot.SceneCharacters.Count > 0)
            {
                sb.AppendLine("在场人物:");

                foreach (var character in snapshot.SceneCharacters)
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

    private static string BuildSystemInjection(RoundReadSnapshot snapshot)
    {
        var sb = new StringBuilder();

        if (snapshot.Scene is not null)
        {
            sb.AppendLine("## 场景信息");
            sb.AppendLine($"时间标签: {snapshot.Scene.TimeLabel}");
            sb.AppendLine($"状态: {snapshot.Scene.Status}");

            if (!string.IsNullOrWhiteSpace(snapshot.Scene.ProgressSummary))
            {
                sb.AppendLine("进展摘要:");
                sb.AppendLine(snapshot.Scene.ProgressSummary);
            }

            sb.AppendLine();
        }

        if (snapshot.GlobalAttributes.Count > 0)
        {
            var valueMap = snapshot.GlobalValues.ToDictionary(item => item.AttributeID);

            sb.AppendLine("## 全局状态");

            foreach (var attr in snapshot.GlobalAttributes)
            {
                var value = valueMap.TryGetValue(attr.ID, out var sv) ?
                                sv :
                                null;
                sb.AppendLine($"- {attr.DisplayName} ({attr.Name}): {value?.Value ?? "未设置"}");
            }

            sb.AppendLine();
        }

        if (snapshot.ActiveDirectives.Count > 0)
        {
            sb.AppendLine("## 生效指令");

            foreach (var directive in snapshot.ActiveDirectives)
            {
                var ttl = directive.TTL.HasValue ?
                              $" (剩余 {directive.TTL} 轮)" :
                              " (永久)";
                sb.AppendLine($"- [{directive.Type}]{directive.Content}{ttl}");
            }

            sb.AppendLine();
        }

        if (snapshot.SceneCharacters.Count > 0)
        {
            sb.AppendLine("## 在场人物");

            foreach (var character in snapshot.SceneCharacters)
                sb.AppendLine($"- {character.Name}: {character.Description}");

            sb.AppendLine();

            InjectCharacterState(sb, snapshot);
            InjectCharacterRelations(sb, snapshot);
        }

        return sb.ToString();
    }

    private static void InjectCharacterState(StringBuilder sb, RoundReadSnapshot snapshot)
    {
        if (snapshot.CharacterAttributes.Count == 0)
            return;

        var values = snapshot.CharacterValues.ToDictionary
        (item => (item.CharacterID, item.AttributeID)
        );

        sb.AppendLine("## 在场人物状态");

        foreach (var character in snapshot.SceneCharacters)
        {
            sb.AppendLine($"{character.Name}:");

            foreach (var attr in snapshot.CharacterAttributes)
            {
                values.TryGetValue((character.ID, attr.ID), out var value);
                sb.AppendLine($"- {attr.DisplayName} ({attr.Name}): {value?.Value ?? "未设置"}");
            }
        }

        sb.AppendLine();
    }

    private static void InjectCharacterRelations(StringBuilder sb, RoundReadSnapshot snapshot)
    {
        var characterIDs = snapshot.SceneCharacters.Select(item => item.ID).ToList();
        var idSet        = characterIDs.ToHashSet();

        var merged = new Dictionary<(long Source, long Target), CharacterRelation>();

        foreach (var r in snapshot.CharacterRelations)
        {
            if (idSet.Contains(r.SourceCharacterID) && idSet.Contains(r.TargetCharacterID))
                merged[(r.SourceCharacterID, r.TargetCharacterID)] = r;
        }

        if (merged.Count == 0)
            return;

        sb.AppendLine("## 人物关系");

        var idToName = snapshot.SceneCharacters.ToDictionary(c => c.ID);

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
