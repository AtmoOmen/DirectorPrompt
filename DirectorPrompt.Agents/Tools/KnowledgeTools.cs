using System.Text.Json;
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

        var entries = await knowledgeRepository.GetActiveEntriesAsync(context.ProjectID);

        if (entries.Count == 0)
        {
            Log.Information("工具调用完成: query_knowledge, 结果=无知识条目");
            return JsonSerializer.Serialize(new { message = "无可用知识" });
        }

        var embeddingService = embeddingServiceFactory.Create(context.EmbeddingConfig);

        var needsRegeneration = entries
                                .Where
                                (e =>
                                    {
                                        var currentHash = EmbeddingConversions.ComputeHash($"{e.Title}\n{e.Content}");
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
                var hash     = EmbeddingConversions.ComputeHash(text);
                var embBytes = EmbeddingConversions.FloatsToBytes(emb);

                await knowledgeRepository.SaveEmbeddingAsync(context.ProjectID, entry.ID, embBytes, hash);
            }

            entries = await knowledgeRepository.GetActiveEntriesAsync(context.ProjectID);
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
}
