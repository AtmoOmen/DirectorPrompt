using System.Text;
using DirectorPrompt.Domain.Models;
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

        var entries = await GetSearchableEntriesAsync(context, cancellationToken);
        var config  = context.KnowledgeConfig;

        if (entries.Count == 0 || config.SemanticTopK <= 0 || config.TokenBudget <= 0)
            return [];

        var embeddingService = embeddingServiceFactory.Create(context.EmbeddingConfig);
        var queryEmbedding   = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        var queryBytes       = EmbeddingConversions.FloatsToBytes(queryEmbedding);
        var candidateIDs     = entries.Select(e => e.ID).ToList();
        var searchResults    = await knowledgeRepository.SearchByVectorAsync
                               (
                                   context.ProjectID,
                                   queryBytes,
                                   config.SemanticTopK,
                                   candidateIDs,
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

    private async Task<IReadOnlyList<KnowledgeEntry>> GetSearchableEntriesAsync
    (
        ToolExecutionContext context,
        CancellationToken    cancellationToken
    )
    {
        var activeEntries = await knowledgeRepository.GetActiveEntriesAsync(context.ProjectID, cancellationToken);

        if (context.PhaseActivatedEntryIDs is not { Count: > 0 })
            return activeEntries;

        var phaseEntries = await knowledgeRepository.GetEntriesByIdsAsync
                           (
                               context.ProjectID,
                               context.PhaseActivatedEntryIDs,
                               cancellationToken
                           );
        var seen   = new HashSet<long>(activeEntries.Select(e => e.ID));
        var merged = new List<KnowledgeEntry>(activeEntries);

        foreach (var entry in phaseEntries)
        {
            if (seen.Add(entry.ID))
                merged.Add(entry);
        }

        return merged;
    }

    private static int EstimateTokens(string text) =>
        (Encoding.UTF8.GetByteCount(text) + 3) / 4;
}
