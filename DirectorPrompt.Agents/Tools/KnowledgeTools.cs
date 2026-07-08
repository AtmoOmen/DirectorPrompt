using System.Text.Json;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Tools;

public sealed class KnowledgeTools
(
    IKnowledgeRepository     knowledgeRepository,
    IEmbeddingServiceFactory embeddingServiceFactory
)
{
    public IList<AIFunction> Create(ToolExecutionContext context) =>
    [
        AIFunctionFactory.Create
        (
            (string query, int? topK) => QueryKnowledgeAsync(context, query, topK),
            "query_knowledge",
            """
            语义检索知识条目
            query: 检索内容
            topK: 返回条数, 默认 8
            """
        )
    ];

    private async Task<string> QueryKnowledgeAsync(ToolExecutionContext context, string query, int? topK)
    {
        Log.Information("工具调用: query_knowledge(query={Query}, topK={TopK})", query, topK);

        var entries = await GetSearchableEntriesAsync(context);

        if (entries.Count == 0)
        {
            Log.Information("工具调用完成: query_knowledge, 结果=无知识条目");
            return JsonSerializer.Serialize(new { message = "无可用知识" });
        }

        var embeddingService = embeddingServiceFactory.Create(context.EmbeddingConfig);

        var fingerprint = context.EmbeddingConfig.Fingerprint;

        var needsRegeneration = entries
                                .Where
                                (e =>
                                    {
                                        var currentHash = EmbeddingConversions.ComputeHash($"{e.Title}\n{e.Content}", fingerprint);
                                        return e.ContentHash != currentHash;
                                    }
                                )
                                .ToList();

        if (needsRegeneration.Count > 0)
        {
            Log.Information
            (
                "知识向量补全: 需生成 {Count}/{Total} 条",
                needsRegeneration.Count,
                entries.Count
            );

            foreach (var entry in needsRegeneration)
            {
                var text     = $"{entry.Title}\n{entry.Content}";
                var emb      = await embeddingService.GenerateEmbeddingAsync(text);
                var hash     = EmbeddingConversions.ComputeHash(text, fingerprint);
                var embBytes = EmbeddingConversions.FloatsToBytes(emb);

                await knowledgeRepository.SaveEmbeddingAsync(context.ProjectID, entry.ID, embBytes, hash);
            }

            entries = await GetSearchableEntriesAsync(context);
        }

        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query);
        var queryBytes     = EmbeddingConversions.FloatsToBytes(queryEmbedding);
        var limit          = topK ?? 8;

        var searchResults = await knowledgeRepository.SearchByVectorAsync
                            (
                                context.ProjectID,
                                queryBytes,
                                limit
                            );

        var entryMap = entries.ToDictionary(e => e.ID);

        var result = searchResults
                     .Where(sr => entryMap.ContainsKey(sr.entryID))
                     .Select
                     (sr =>
                         {
                             var entry = entryMap[sr.entryID];
                             return new
                             {
                                 title     = entry.Title,
                                 content   = entry.Content,
                                 tags      = entry.Tags,
                                 relevance = Math.Round(1f - sr.distance, 4)
                             };
                         }
                     )
                     .ToList();

        Log.Information("工具调用完成: query_knowledge, 返回条目数={Count}", result.Count);

        return JsonSerializer.Serialize(result);
    }

    private async Task<IReadOnlyList<KnowledgeEntry>> GetSearchableEntriesAsync(ToolExecutionContext context)
    {
        var activeEntries = await knowledgeRepository.GetActiveEntriesAsync(context.ProjectID);

        if (context.PhaseActivatedEntryIDs is not { Count: > 0 })
            return activeEntries;

        var phaseEntries = await knowledgeRepository.GetEntriesByIdsAsync(context.ProjectID, context.PhaseActivatedEntryIDs);

        var seen   = new HashSet<long>(activeEntries.Select(e => e.ID));
        var merged = new List<KnowledgeEntry>(activeEntries);

        foreach (var entry in phaseEntries)
        {
            if (seen.Add(entry.ID))
                merged.Add(entry);
        }

        return merged;
    }
}
