using DirectorPrompt.Agents;
using DirectorPrompt.Agents.Retrieval;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Xunit;

namespace DirectorPrompt.Tests;

public sealed class MemoryRecallQualityTests
{
    [Fact]
    public async Task BoundedCandidatesRetainAtLeastNinetyFivePercentOfFullTopK()
    {
        const int   TOP_K            = 20;
        const long  CURRENT_TIMELINE = 10_000_000;
        const float DECAY            = 0.01f;
        var memories = Enumerable.Range(1, 1000)
                                 .Select
                                 (id => new MemoryEntry
                                     {
                                         ID          = id,
                                         ProjectID   = 1,
                                         SessionID   = 1,
                                         SceneID     = 1,
                                         TimelinePos = CURRENT_TIMELINE - (id % 50 * 1000),
                                         Content     = $"memory-{id}"
                                     }
                                 )
                                 .ToList();
        var vectorResults = memories.Select
                                    (memory => new VectorSearchResult
                                     (
                                         memory.ID,
                                         "content",
                                         memory.ID / 2000f
                                     )
                                    )
                                    .ToList();
        var repository = new BoundedMemoryRepository(memories, vectorResults);
        var service    = new MemoryRetrievalService(repository, new UnusedEmbeddingServiceFactory());
        var context = new ToolExecutionContext
        (
            1,
            1,
            1,
            CURRENT_TIMELINE,
            1,
            new ResolvedEmbeddingConfig(),
            new KnowledgeRetrievalConfig(),
            new MemoryConfig
            {
                RecallTopK      = TOP_K,
                TokenBudget     = 100_000,
                TimeDecayLambda = DECAY
            }
        );
        var expectedIDs = vectorResults.Select
                                       (result =>
                                           {
                                               var memory = memories[(int)result.EntryID - 1];
                                               var sceneDistance = (CURRENT_TIMELINE - memory.TimelinePos) /
                                                                   (double)TimelineCalculator.GAP;
                                               return
                                                   (
                                                       result.EntryID,
                                                       Score: (1f - result.Distance) * Math.Exp(-DECAY * sceneDistance)
                                                   );
                                           }
                                       )
                                       .OrderByDescending(item => item.Score)
                                       .Take(TOP_K)
                                       .Select(item => item.EntryID)
                                       .ToHashSet();

        var actual  = await service.SearchAsync(context, new byte[sizeof(float)]);
        var overlap = actual.Count(result => expectedIDs.Contains(result.ID)) / (double)TOP_K;

        Assert.Equal(320, repository.RequestedTopK);
        Assert.True(overlap >= 0.95, $"TopK overlap was {overlap:P1}");
    }

    private sealed class BoundedMemoryRepository
    (
        IReadOnlyList<MemoryEntry>        memories,
        IReadOnlyList<VectorSearchResult> vectorResults
    ) : IMemoryRepository
    {
        public int RequestedTopK { get; private set; }

        public Task<IReadOnlyList<MemoryEntry>> GetByIdsAsync
        (
            long                sessionID,
            IReadOnlyList<long> memoryIDs,
            CancellationToken   cancellationToken = default
        )
        {
            var ids = memoryIDs.ToHashSet();
            return Task.FromResult<IReadOnlyList<MemoryEntry>>
            (
                memories.Where(memory => memory.SessionID == sessionID && ids.Contains(memory.ID)).ToList()
            );
        }

        public Task<IReadOnlyList<VectorSearchResult>> SearchByVectorAsync
        (
            long              projectID,
            long              sessionID,
            long              maxTimelinePosition,
            byte[]            queryVector,
            int               topK,
            CancellationToken cancellationToken = default
        )
        {
            RequestedTopK = topK;
            return Task.FromResult<IReadOnlyList<VectorSearchResult>>(vectorResults.Take(topK).ToList());
        }

        public Task<MemoryEntry?> GetByIDAsync(long id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MemoryEntry>> GetPendingIndexEntriesAsync
        (
            long              projectID,
            string            embeddingFingerprint,
            int               limit,
            CancellationToken cancellationToken = default
        ) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MemoryEntry>> GetRecentByCharacterAsync
        (
            long              characterID,
            long              maxTimelinePos,
            int               limit,
            CancellationToken cancellationToken = default
        ) =>
            throw new NotSupportedException();

        public Task<MemoryPage> GetPageAsync
        (
            MemoryPageQuery   query,
            CancellationToken cancellationToken = default
        ) =>
            throw new NotSupportedException();

        public Task<MemoryEntry> CreateAsync
        (
            MemoryEntry       entry,
            CancellationToken cancellationToken = default
        ) =>
            throw new NotSupportedException();

        public Task UpdateAsync(MemoryEntry entry, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MemoryEntry> MergeAsync
        (
            IReadOnlyList<long> memoryIDs,
            long                sceneID,
            string              content,
            string[]            tags,
            CancellationToken   cancellationToken = default
        ) =>
            throw new NotSupportedException();

        public Task DeleteAsync(long id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveEmbeddingsAsync
        (
            long                                             projectID,
            long                                             entryID,
            long                                             sessionID,
            long                                             timelinePosition,
            IReadOnlyList<(string source, byte[] embedding)> vectors,
            string                                           contentHash,
            string                                           embeddingFingerprint,
            CancellationToken                                cancellationToken = default
        ) =>
            throw new NotSupportedException();

        public Task DeleteEmbeddingAsync
        (
            long              projectID,
            long              entryID,
            CancellationToken cancellationToken = default
        ) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedEmbeddingServiceFactory : IEmbeddingServiceFactory
    {
        public IEmbeddingService Create(ResolvedEmbeddingConfig config) =>
            throw new NotSupportedException();

        public void Reset()
        {
        }
    }
}
