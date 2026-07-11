using System.Text.Json;
using DirectorPrompt.Domain.Configurations;
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
            (string query, int? topK = null) => QueryKnowledgeAsync(context, query, topK),
            "query_knowledge",
            """
            语义检索知识条目
            query: 检索内容
            topK: 返回条数 (可选)
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
                                        var currentHash = ComputeEntryHash(e, fingerprint);
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

            var entryTexts = needsRegeneration
                             .Select(e => (entry: e, texts: BuildEmbeddingTexts(e)))
                             .Where(et => et.texts.Count > 0)
                             .ToList();

            if (entryTexts.Count > 0)
            {
                var allTexts   = entryTexts.SelectMany(et => et.texts.Select(t => t.text)).ToList();
                var embeddings = await embeddingService.GenerateEmbeddingsAsync(allTexts);

                var offset = 0;

                foreach (var (entry, texts) in entryTexts)
                {
                    var hash    = ComputeEntryHash(entry, fingerprint);
                    var vectors = new List<(string source, byte[] embedding)>();

                    for (var i = 0; i < texts.Count; i++)
                    {
                        var embBytes = EmbeddingConversions.FloatsToBytes(embeddings[offset + i]);
                        vectors.Add((texts[i].source, embBytes));
                    }

                    await knowledgeRepository.SaveEmbeddingsAsync(context.ProjectID, entry.ID, vectors, hash);

                    offset += texts.Count;
                }
            }

            entries = await GetSearchableEntriesAsync(context);
        }

        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query);
        var queryBytes     = EmbeddingConversions.FloatsToBytes(queryEmbedding);

        var config = context.KnowledgeConfig;
        var limit  = topK ?? config.SemanticTopK;

        var searchResults = await knowledgeRepository.SearchByVectorAsync
                            (
                                context.ProjectID,
                                queryBytes,
                                limit
                            );

        Log.Information
        (
            "知识搜索结果: 搜索返回={SearchCount}, entryMap大小={EntryCount}, MinRelevance={MinRelevance}",
            searchResults.Count,
            entries.Count,
            config.MinRelevance
        );

        var entryMap = entries.ToDictionary(e => e.ID);

        var matched = searchResults.Where(sr => entryMap.ContainsKey(sr.entryID)).ToList();

        foreach (var sr in matched)
        {
            var entry = entryMap[sr.entryID];
            Log.Information
            (
                "知识搜索详情: entryID={EntryID}, remarks={Remarks}, distance={Distance}, relevance={Relevance}",
                sr.entryID,
                entry.Remarks,
                sr.distance,
                Math.Round(1f - sr.distance, 4)
            );
        }

        Log.Information
        (
            "知识搜索匹配: entryMap命中={Matched}, 未命中={Unmatched}",
            matched.Count,
            searchResults.Count - matched.Count
        );

        var minRelevance = config.MinRelevance;
        var tokenBudget  = config.TokenBudget;
        var usedTokens   = 0;

        var result = searchResults
                     .Where(sr => entryMap.ContainsKey(sr.entryID))
                     .Select
                     (sr =>
                         {
                             var entry = entryMap[sr.entryID];
                             return new
                             {
                                 remarks   = entry.Remarks,
                                 content   = entry.Content,
                                 keywords  = entry.Keywords,
                                 relevance = Math.Round(1f - sr.distance, 4)
                             };
                         }
                     )
                     .Where
                     (r =>
                         {
                             if (minRelevance > 0 && r.relevance < minRelevance)
                                 return false;

                             var tokens = EstimateTokens(r.content);

                             if (usedTokens + tokens > tokenBudget)
                                 return false;

                             usedTokens += tokens;
                             return true;
                         }
                     )
                     .ToList();

        Log.Information("工具调用完成: query_knowledge, 返回条目数={Count}", result.Count);

        return JsonSerializer.Serialize(result);
    }

    private static IReadOnlyList<(string source, string text)> BuildEmbeddingTexts(KnowledgeEntry entry)
    {
        var texts = new List<(string source, string text)>();

        foreach (var keyword in entry.Keywords)
        {
            if (!string.IsNullOrWhiteSpace(keyword))
                texts.Add(("keyword", keyword.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(entry.Content))
            texts.Add(("content", entry.Content));

        return texts;
    }

    private static string ComputeEntryHash(KnowledgeEntry entry, string fingerprint)
    {
        var texts    = BuildEmbeddingTexts(entry);
        var combined = string.Join('\0', texts.Select(t => t.text));

        return EmbeddingConversions.ComputeHash(combined, fingerprint);
    }

    private static int EstimateTokens(string text) =>
        text.Length / 4;

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
