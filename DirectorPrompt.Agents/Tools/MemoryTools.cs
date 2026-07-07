using System.Text.Json;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Tools;

public sealed class MemoryTools
(
    IMemoryRepository        memoryRepository,
    IEmbeddingServiceFactory embeddingServiceFactory
)
{
    public IList<AIFunction> Create(ToolExecutionContext context) =>
    [
        AIFunctionFactory.Create
        (
            (string query, string? tags, int? topK) => QueryMemoryAsync(context, query, tags, topK),
            "query_memory",
            """
            语义检索记忆条目
            query: 检索内容
            tags: 按标签过滤, 逗号分隔 (可选)
            topK: 返回条数, 默认 10
            """
        ),
        AIFunctionFactory.Create
        (
            (long sceneID, string content, string tags) =>
                CreateMemoryAsync(context, sceneID, content, tags),
            "create_memory",
            """
            创建新记忆
            sceneID: 归属场景 ID
            content: 记忆正文
            tags: 标签 (逗号分隔)
            """
        ),
        AIFunctionFactory.Create
        (
            (long memoryID, string content, string? tags) =>
                UpdateMemoryAsync(context, memoryID, content, tags),
            "update_memory",
            """
            改写已有记忆
            memoryID: 记忆 ID
            content: 新内容
            tags: 新标签, 逗号分隔 (可选)
            """
        ),
        AIFunctionFactory.Create
        (
            (string memoryIDs, string content, string tags) =>
                MergeMemoriesAsync(context, memoryIDs, content, tags),
            "merge_memories",
            """
            合并多条记忆为一条
            memoryIDs: 要合并的记忆 ID 列表 (逗号分隔)
            content: 合并后的内容
            tags: 标签 (逗号分隔)
            """
        )
    ];

    private async Task<string> QueryMemoryAsync
    (
        ToolExecutionContext context,
        string               query,
        string?              tags,
        int?                 topK
    )
    {
        Log.Information("工具调用: query_memory(query={Query}, tags={Tags}, topK={TopK})", query, tags, topK);

        var memories = await memoryRepository.GetBySessionAsync(context.SessionID, context.TimelinePosition);

        if (memories.Count == 0)
        {
            Log.Information("工具调用完成: query_memory, 结果=无记忆条目");
            return JsonSerializer.Serialize(new { message = "无可用记忆" });
        }

        IEnumerable<MemoryEntry> candidates = memories;

        if (!string.IsNullOrWhiteSpace(tags))
        {
            var tagList = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            candidates = candidates.Where(m => m.Tags.Any(t => tagList.Contains(t)));
        }

        var candidateList = candidates.ToList();

        if (candidateList.Count == 0)
        {
            Log.Information("工具调用完成: query_memory, 结果=无匹配记忆条目");
            return JsonSerializer.Serialize(new { message = "无可用记忆" });
        }

        var embeddingService = embeddingServiceFactory.Create(context.EmbeddingConfig);

        var needsRegeneration = candidateList
                                .Where(m =>
                                {
                                    var currentHash = EmbeddingConversions.ComputeHash(m.Content);
                                    return m.ContentHash != currentHash;
                                })
                                .ToList();

        if (needsRegeneration.Count > 0)
        {
            Log.Information
            (
                "记忆向量补全: 需生成 {Count}/{Total} 条",
                needsRegeneration.Count,
                candidateList.Count
            );

            foreach (var memory in needsRegeneration)
            {
                var emb      = await embeddingService.GenerateEmbeddingAsync(memory.Content);
                var hash     = EmbeddingConversions.ComputeHash(memory.Content);
                var embBytes = EmbeddingConversions.FloatsToBytes(emb);

                await memoryRepository.SaveEmbeddingAsync(context.ProjectID, memory.ID, embBytes, hash);
            }

            memories = await memoryRepository.GetBySessionAsync(context.SessionID, context.TimelinePosition);

            candidates = memories;

            if (!string.IsNullOrWhiteSpace(tags))
            {
                var tagList = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                candidates = candidates.Where(m => m.Tags.Any(t => tagList.Contains(t)));
            }

            candidateList = candidates.ToList();
        }

        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query);
        var queryBytes     = EmbeddingConversions.FloatsToBytes(queryEmbedding);
        var limit          = topK ?? 10;

        var candidateIDs = candidateList.Select(m => m.ID).ToList();

        var searchResults = await memoryRepository.SearchByVectorAsync
                            (
                                context.ProjectID,
                                queryBytes,
                                limit,
                                candidateIDs
                            );

        var memoryMap = candidateList.ToDictionary(m => m.ID);

        var result = searchResults
                     .Where(sr => memoryMap.ContainsKey(sr.entryID))
                     .Select
                     (sr =>
                         {
                             var m = memoryMap[sr.entryID];
                             return new
                             {
                                 id       = m.ID,
                                 content  = m.Content,
                                 tags     = m.Tags,
                                 sceneID  = m.SceneID,
                                 relevance = Math.Round(1f - sr.distance, 4)
                             };
                         }
                     )
                     .ToList();

        Log.Information("工具调用完成: query_memory, 返回条目数={Count}", result.Count);

        return JsonSerializer.Serialize(result);
    }

    private async Task<string> CreateMemoryAsync
    (
        ToolExecutionContext context,
        long                 sceneID,
        string               content,
        string               tags
    )
    {
        Log.Information("工具调用: create_memory(sceneID={SceneID}, content={Content})", sceneID, content.Length > 100 ? content[..100] + "..." : content);
        var tagList = ParseTags(tags);

        var entry = new MemoryEntry
        {
            ProjectID   = context.ProjectID,
            SessionID   = context.SessionID,
            SceneID     = sceneID,
            TimelinePos = context.TimelinePosition,
            Content     = content,
            Tags        = tagList
        };

        var created = await memoryRepository.CreateAsync(entry);

        Log.Information("工具调用完成: create_memory, memoryID={ID}", created.ID);

        return JsonSerializer.Serialize(new { memoryID = created.ID });
    }

    private async Task<string> UpdateMemoryAsync
    (
        ToolExecutionContext context,
        long                 memoryID,
        string               content,
        string?              tags
    )
    {
        Log.Information("工具调用: update_memory(memoryID={MemoryID})", memoryID);
        var existing = await memoryRepository.GetByIDAsync(memoryID);

        if (existing is null)
            return JsonSerializer.Serialize(new { error = $"记忆 {memoryID} 不存在" });

        var updated = existing with
        {
            Content = content,
            Tags = string.IsNullOrWhiteSpace(tags) ?
                       existing.Tags :
                       ParseTags(tags)
        };

        await memoryRepository.UpdateAsync(updated);

        return JsonSerializer.Serialize(new { memoryID, success = true });
    }

    private async Task<string> MergeMemoriesAsync
    (
        ToolExecutionContext context,
        string               memoryIDs,
        string               content,
        string               tags
    )
    {
        Log.Information("工具调用: merge_memories(memoryIDs={MemoryIDs})", memoryIDs);
        var idList = memoryIDs
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(long.Parse)
                     .ToList();

        var tagList = ParseTags(tags);
        var merged  = await memoryRepository.MergeAsync(idList, context.SceneID ?? 0, content, tagList);

        return JsonSerializer.Serialize(new { memoryID = merged.ID });
    }

    private static string[] ParseTags(string tags) =>
        tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
