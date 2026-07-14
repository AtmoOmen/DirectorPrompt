using System.Text;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Serilog;

namespace DirectorPrompt.Agents.Retrieval;

public sealed class KnowledgeRetrievalService
(
    IKnowledgeRepository     knowledgeRepository,
    IEmbeddingServiceFactory embeddingServiceFactory
)
{
    public async Task<IReadOnlyList<KnowledgeRetrievalResult>> SearchAsync
    (
        ToolExecutionContext context,
        string               query,
        CancellationToken    cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("检索内容不能为空", nameof(query));

        var config = context.KnowledgeConfig;

        if (config.SemanticTopK <= 0 || config.TokenBudget <= 0)
            return [];

        var embeddingService = embeddingServiceFactory.Create(context.EmbeddingConfig);
        var queryEmbedding   = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        var queryBytes       = EmbeddingConversions.FloatsToBytes(queryEmbedding);
        return await SearchAsync(context, queryBytes, cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeRetrievalResult>> SearchAsync
    (
        ToolExecutionContext context,
        byte[]               queryVector,
        CancellationToken    cancellationToken = default
    )
    {
        var config = context.KnowledgeConfig;

        if (config.SemanticTopK <= 0 || config.TokenBudget <= 0)
            return [];

        var candidateLimit = Math.Max(128, config.SemanticTopK * 16);
        var searchResults = await knowledgeRepository.SearchByVectorAsync
                            (
                                context.ProjectID,
                                queryVector,
                                candidateLimit,
                                cancellationToken
                            );
        var candidateIDs  = searchResults.Select(result => result.EntryID).Distinct().ToList();
        var phaseEntryIDs = context.PhaseActivatedEntryIDs ?? [];
        var entries = await knowledgeRepository.GetSearchableEntriesByIdsAsync
                      (
                          context.ProjectID,
                          candidateIDs,
                          phaseEntryIDs,
                          cancellationToken
                      );
        var entryMap   = entries.ToDictionary(e => e.ID);
        var usedTokens = 0;

        var result = searchResults
                     .Where(r => entryMap.ContainsKey(r.EntryID))
                     .Select
                     (r =>
                         {
                             var entry      = entryMap[r.EntryID];
                             var similarity = 1f - r.Distance;

                             return new KnowledgeRetrievalResult
                             (
                                 entry.ID,
                                 entry.Remarks,
                                 entry.Content,
                                 entry.Keywords,
                                 r.Source,
                                 similarity
                             );
                         }
                     )
                     .Where(r => config.MinRelevance <= 0 || r.SemanticSimilarity >= config.MinRelevance)
                     .OrderByDescending(result => result.SemanticSimilarity)
                     .Take(config.SemanticTopK)
                     .Where
                     (r =>
                         {
                             var tokens = EstimateTokens(r.Content);

                             if (usedTokens + tokens > config.TokenBudget)
                                 return false;

                             usedTokens += tokens;
                             return true;
                         }
                     )
                     .ToList();

        Log.Information("知识检索完成: 候选={CandidateCount}, 返回={ResultCount}", entries.Count, result.Count);

        return result;
    }

    private static int EstimateTokens(string text) =>
        (Encoding.UTF8.GetByteCount(text) + 3) / 4;
}
