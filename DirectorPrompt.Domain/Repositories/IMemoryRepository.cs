using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Repositories;

public interface IMemoryRepository
{
    Task<MemoryEntry?> GetByIDAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryEntry>> GetPendingIndexEntriesAsync
    (
        long              projectID,
        string            embeddingFingerprint,
        int               limit,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<MemoryEntry>> GetByIdsAsync
    (
        long                sessionID,
        IReadOnlyList<long> memoryIDs,
        CancellationToken   cancellationToken = default
    );

    Task<IReadOnlyList<MemoryEntry>> GetRecentByCharacterAsync
    (
        long              characterID,
        long              maxTimelinePos,
        int               limit,
        CancellationToken cancellationToken = default
    );

    Task<MemoryPage> GetPageAsync(MemoryPageQuery query, CancellationToken cancellationToken = default);

    Task<MemoryEntry> CreateAsync(MemoryEntry entry, long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task UpdateAsync(MemoryEntry entry, long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task<MemoryEntry> MergeAsync
        (IReadOnlyList<long> memoryIDs, long sceneID, string content, string[] tags, long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task SaveEmbeddingsAsync
    (
        long                                             projectID,
        long                                             entryID,
        long                                             sessionID,
        long                                             timelinePosition,
        IReadOnlyList<(string source, byte[] embedding)> vectors,
        string                                           contentHash,
        string                                           embeddingFingerprint,
        CancellationToken                                cancellationToken = default
    );

    Task DeleteEmbeddingAsync(long projectID, long entryID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorSearchResult>> SearchByVectorAsync
    (
        long              projectID,
        long              sessionID,
        long              maxTimelinePosition,
        byte[]            queryVector,
        int               topK,
        CancellationToken cancellationToken = default
    );
}
