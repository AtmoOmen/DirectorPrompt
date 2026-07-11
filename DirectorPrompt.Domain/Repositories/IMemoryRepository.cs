using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Repositories;

public interface IMemoryRepository
{
    Task<MemoryEntry?> GetByIDAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryEntry>> GetBySessionAsync(long sessionID, long maxTimelinePos, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryEntry>> GetBySceneAsync(long sceneID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryEntry>> GetByCharacterAsync(long characterID, long maxTimelinePos, CancellationToken cancellationToken = default);

    Task<MemoryEntry> CreateAsync(MemoryEntry entry, CancellationToken cancellationToken = default);

    Task UpdateAsync(MemoryEntry entry, CancellationToken cancellationToken = default);

    Task<MemoryEntry> MergeAsync(IReadOnlyList<long> memoryIDs, long sceneID, string content, string[] tags, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, CancellationToken cancellationToken = default);

    Task SaveEmbeddingsAsync
    (
        long                                           projectID,
        long                                           entryID,
        IReadOnlyList<(string source, byte[] embedding)> vectors,
        string                                         contentHash,
        CancellationToken                               cancellationToken = default
    );

    Task DeleteEmbeddingAsync(long projectID, long entryID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(long entryID, float distance)>> SearchByVectorAsync
    (
        long                 projectID,
        byte[]               queryVector,
        int                  topK,
        IReadOnlyList<long>? candidateIDs      = null,
        CancellationToken    cancellationToken = default
    );
}
