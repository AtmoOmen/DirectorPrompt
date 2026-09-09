using DirectorPrompt.Agents.Retrieval;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Tools;

public sealed class MemoryTools
(
    IMemoryRepository      memoryRepository,
    ICharacterRepository   characterRepository,
    MemoryRetrievalService retrievalService,
    EmbeddingIndexService  embeddingIndexService
)
{
    public IList<AIFunction> Create(ToolExecutionContext context) =>
    [
        AIFunctionFactory.Create
        (
            (string query) => QueryMemoryAsync(context, query),
            "query_memory",
            """
            语义检索记忆条目
            query: 自包含的检索内容, 明确写出相关人物、地点和事件, 不使用指代词
            返回记忆原文、命中来源、语义相似度、时间权重和最终分数, 结果已由系统完成筛选
            """
        ),
        AIFunctionFactory.Create
        (
            (string characterNames) => QueryMemoryByCharacterAsync(context, characterNames),
            "query_memory_by_character",
            """
            按人物查询相关记忆
            characterNames: 人物名列表 (逗号分隔)
            """
        ),
        AIFunctionFactory.Create
        (
            (string content, string tags, string? characterNames = null) =>
                CreateMemoryAsync(context, content, tags, characterNames),
            "create_memory",
            """
            创建新记忆
            content: 记忆正文
            tags: 标签 (逗号分隔)
            characterNames: 涉及人物名列表 (逗号分隔, 可选)
            """
        ),
        AIFunctionFactory.Create
        (
            (long memoryID, string content, string? tags = null, string? characterNames = null) =>
                UpdateMemoryAsync(context, memoryID, content, tags, characterNames),
            "update_memory",
            """
            改写已有记忆
            memoryID: 记忆 ID, 取自 query_memory 返回的 id
            content: 新内容
            tags: 新标签, 逗号分隔 (可选)
            characterNames: 涉及人物名列表 (逗号分隔, 可选)
            """
        ),
        AIFunctionFactory.Create
        (
            (string memoryIDs, string content, string tags, string? characterNames = null) =>
                MergeMemoriesAsync(context, memoryIDs, content, tags, characterNames),
            "merge_memories",
            """
            合并多条记忆为一条
            memoryIDs: 要合并的记忆 ID 列表 (逗号分隔, 取自 query_memory 返回的 id)
            content: 合并后的内容
            tags: 标签 (逗号分隔)
            characterNames: 涉及人物名列表 (逗号分隔, 可选)
            """
        )
    ];

    private async Task<string> QueryMemoryAsync(ToolExecutionContext context, string query)
    {
        Log.Information("工具调用: query_memory(query={Query})", query);

        if (string.IsNullOrWhiteSpace(query))
            return ToolResult.Error("检索内容不能为空");

        var results = await retrievalService.SearchAsync(context, query);
        var response = results.Select
        (r =>
             new
             {
                 id                 = r.ID,
                 content            = r.Content,
                 tags               = r.Tags,
                 sceneID            = r.SceneID,
                 matchedSource      = r.MatchedSource,
                 semanticSimilarity = Math.Round(r.SemanticSimilarity, 4),
                 recencyWeight      = Math.Round(r.RecencyWeight,      4),
                 finalScore         = Math.Round(r.FinalScore,         4)
             }
        );

        Log.Information("工具调用完成: query_memory, 返回条目数={Count}", results.Count);

        return ToolResult.Data(response);
    }

    private async Task<string> QueryMemoryByCharacterAsync
    (
        ToolExecutionContext context,
        string               characterNames
    )
    {
        Log.Information("工具调用: query_memory_by_character(characterNames={Names})", characterNames);

        var (idList, error) = await ResolveCharacterIDsAsync(context, characterNames);

        if (error is not null)
            return error;

        var result = new List<object>();

        foreach (var characterID in idList)
        {
            var memories = await memoryRepository.GetRecentByCharacterAsync
                           (
                               characterID,
                               context.TimelinePosition,
                               100
                           );

            foreach (var memory in memories)
            {
                result.Add
                (
                    new
                    {
                        id       = memory.ID,
                        content  = memory.Content,
                        tags     = memory.Tags,
                        sceneID  = memory.SceneID,
                        timeline = memory.TimelinePos
                    }
                );
            }
        }

        var distinct = result
                       .GroupBy(r => ((dynamic)r).id)
                       .Select(g => g.First())
                       .ToList();

        Log.Information("工具调用完成: query_memory_by_character, 返回条目数={Count}", distinct.Count);

        return ToolResult.Data(distinct);
    }

    private async Task<string> CreateMemoryAsync
    (
        ToolExecutionContext context,
        string               content,
        string               tags,
        string?              characterNames
    )
    {
        Log.Information
        (
            "工具调用: create_memory(sceneID={SceneID}, length={Length})",
            context.SceneID,
            content.Length
        );

        var (characterList, error) = await ResolveCharacterIDsAsync(context, characterNames);

        if (error is not null)
            return error;

        var entry = new MemoryEntry
        {
            ProjectID           = context.ProjectID,
            SessionID           = context.SessionID,
            SceneID             = context.SceneID ?? 0,
            TimelinePos         = context.TimelinePosition,
            Content             = content,
            Tags                = ParseTags(tags),
            RelatedCharacterIDs = characterList
        };
        var created = await memoryRepository.CreateAsync(entry, context.SessionID, context.RoundID);

        await embeddingIndexService.IndexMemoriesAsync([created], context.EmbeddingConfig);

        foreach (var characterID in characterList)
            await characterRepository.TouchAsync(characterID, context.RoundID, context.SessionID);

        Log.Information("工具调用完成: create_memory, memoryID={ID}", created.ID);

        return ToolResult.Data(new { memoryID = created.ID });
    }

    private async Task<string> UpdateMemoryAsync
    (
        ToolExecutionContext context,
        long                 memoryID,
        string               content,
        string?              tags,
        string?              characterNames
    )
    {
        Log.Information("工具调用: update_memory(memoryID={MemoryID})", memoryID);

        var existing = await memoryRepository.GetByIDAsync(memoryID);

        if (existing is null)
            return ToolResult.Error($"记忆 {memoryID} 不存在");

        var parsedCharacterIDs = existing.RelatedCharacterIDs;

        if (!string.IsNullOrWhiteSpace(characterNames))
        {
            var (characterList, error) = await ResolveCharacterIDsAsync(context, characterNames);

            if (error is not null)
                return error;

            parsedCharacterIDs = characterList;
        }

        var updated = existing with
        {
            Content = content,
            Tags = string.IsNullOrWhiteSpace(tags) ?
                       existing.Tags :
                       ParseTags(tags),
            RelatedCharacterIDs = parsedCharacterIDs
        };

        await memoryRepository.UpdateAsync(updated, context.SessionID, context.RoundID);
        await embeddingIndexService.IndexMemoriesAsync([updated], context.EmbeddingConfig);

        foreach (var characterID in parsedCharacterIDs)
            await characterRepository.TouchAsync(characterID, context.RoundID, context.SessionID);

        return ToolResult.Data(new { memoryID });
    }

    private async Task<string> MergeMemoriesAsync
    (
        ToolExecutionContext context,
        string               memoryIDs,
        string               content,
        string               tags,
        string?              characterNames
    )
    {
        Log.Information("工具调用: merge_memories(memoryIDs={MemoryIDs})", memoryIDs);

        var (charList, error) = await ResolveCharacterIDsAsync(context, characterNames);

        if (error is not null)
            return error;

        var (idList, idError) = ParseMemoryIDs(memoryIDs);

        if (idError is not null)
            return idError;

        var merged = await memoryRepository.MergeAsync(idList, context.SceneID ?? 0, content, ParseTags(tags), context.SessionID, context.RoundID);

        if (charList.Length > 0)
        {
            merged = merged with { RelatedCharacterIDs = charList };
            await memoryRepository.UpdateAsync(merged, context.SessionID, context.RoundID);

            foreach (var characterID in charList)
                await characterRepository.TouchAsync(characterID, context.RoundID, context.SessionID);
        }

        foreach (var memoryID in idList)
            await memoryRepository.DeleteEmbeddingAsync(context.ProjectID, memoryID);

        await embeddingIndexService.IndexMemoriesAsync([merged], context.EmbeddingConfig);

        return ToolResult.Data(new { memoryID = merged.ID });
    }

    private static string[] ParseTags(string tags) =>
        tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static (List<long> IDs, string? Error) ParseMemoryIDs(string memoryIDs)
    {
        var ids = new List<long>();

        foreach (var item in memoryIDs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!long.TryParse(item, out var memoryID))
                return ([], ToolResult.Error($"记忆 ID {item} 不是数字"));

            ids.Add(memoryID);
        }

        return ids.Count == 0 ?
                   ([], ToolResult.Error("memoryIDs 不能为空")) :
                   (ids, null);
    }

    private async Task<(long[] IDs, string? Error)> ResolveCharacterIDsAsync
    (
        ToolExecutionContext context,
        string?              characterNames
    )
    {
        if (string.IsNullOrWhiteSpace(characterNames))
            return ([], null);

        var names = characterNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids   = new long[names.Length];

        for (var i = 0; i < names.Length; i++)
        {
            var character = await characterRepository.GetByNameAsync(context.SessionID, names[i]);

            if (character is null)
                return ([], ToolResult.Error($"人物 {names[i]} 不存在"));

            ids[i] = character.ID;
        }

        return (ids, null);
    }
}
