using System.Text;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Serilog;

namespace DirectorPrompt.Agents.Retrieval;

public sealed class MemoryRetrievalService
(
    IMemoryRepository        memoryRepository,
    IEmbeddingServiceFactory embeddingServiceFactory
)
{
    public async Task<IReadOnlyList<MemoryRetrievalResult>> SearchAsync
    (
        ToolExecutionContext context,
        string               query,
        CancellationToken    cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("检索内容不能为空", nameof(query));

        var config = context.MemoryConfig;

        if (config.RecallTopK <= 0 || config.TokenBudget <= 0)
            return [];

        var embeddingService = embeddingServiceFactory.Create(context.EmbeddingConfig);
        var queryEmbedding   = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        var queryBytes       = EmbeddingConversions.FloatsToBytes(queryEmbedding);
        return await SearchAsync(context, queryBytes, cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryRetrievalResult>> SearchAsync
    (
        ToolExecutionContext context,
        byte[]               queryVector,
        CancellationToken    cancellationToken = default
    )
    {
        var config = context.MemoryConfig;

        if (config.RecallTopK <= 0 || config.TokenBudget <= 0)
            return [];

        var candidateLimit = Math.Max(128, config.RecallTopK * 16);
        var searchResults = await memoryRepository.SearchByVectorAsync
                            (
                                context.ProjectID,
                                context.SessionID,
                                context.TimelinePosition,
                                queryVector,
                                candidateLimit,
                                cancellationToken
                            );
        var memories = await memoryRepository.GetByIdsAsync
                       (
                           context.SessionID,
                           searchResults.Select(result => result.EntryID).Distinct().ToList(),
                           cancellationToken
                       );
        var memoryMap = memories.ToDictionary(m => m.ID);
        var lambda    = config.TimeDecayLambda;
        var ranked = searchResults
                     .Where(r => memoryMap.ContainsKey(r.EntryID))
                     .Select
                     (r =>
                         {
                             var memory             = memoryMap[r.EntryID];
                             var semanticSimilarity = 1f - r.Distance;
                             var sceneDistance      = (context.TimelinePosition - memory.TimelinePos) / (double)TimelineCalculator.GAP;
                             var recencyWeight = lambda > 0 ?
                                                     Math.Exp(-lambda * sceneDistance) :
                                                     1d;
                             var finalScore = semanticSimilarity * recencyWeight;

                             return new MemoryRetrievalResult
                             (
                                 memory.ID,
                                 memory.Content,
                                 memory.Tags,
                                 memory.SceneID,
                                 r.Source,
                                 semanticSimilarity,
                                 recencyWeight,
                                 finalScore
                             );
                         }
                     )
                     .Where(r => config.MinRelevance <= 0 || r.FinalScore >= config.MinRelevance)
                     .OrderByDescending(r => r.FinalScore);
        var usedTokens = 0;
        var result = ranked
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
                     .Take(config.RecallTopK)
                     .ToList();

        Log.Information("记忆检索完成: 候选={CandidateCount}, 返回={ResultCount}", memories.Count, result.Count);

        return result;
    }

    private static int EstimateTokens(string text) =>
        (Encoding.UTF8.GetByteCount(text) + 3) / 4;
}
