using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Repositories;

public interface IKnowledgeRepository
{
    Task<KnowledgeEntry?> GetByIDAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeEntry>> GetByProjectAsync(long projectID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeEntry>> GetActiveEntriesAsync(long projectID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeEntry>> GetByGroupAsync(long groupID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeEntry>> GetEntriesByIdsAsync(long projectID, IReadOnlyList<long> entryIDs, CancellationToken cancellationToken = default);

    Task<KnowledgeEntry> CreateAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default);

    Task UpdateAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeGroup>> GetGroupsAsync(long projectID, CancellationToken cancellationToken = default);

    Task<KnowledgeGroup> CreateGroupAsync(KnowledgeGroup group, CancellationToken cancellationToken = default);

    Task UpdateGroupAsync(KnowledgeGroup group, CancellationToken cancellationToken = default);

    Task DeleteGroupAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeEntityIndex>> GetEntityIndexAsync(long projectID, CancellationToken cancellationToken = default);

    Task AddEntityIndexAsync(long entryID, string entityName, CancellationToken cancellationToken = default);

    Task RemoveEntityIndexAsync(long entryID, CancellationToken cancellationToken = default);

    Task SaveEmbeddingsAsync
    (
        long                                             projectID,
        long                                             entryID,
        IReadOnlyList<(string source, byte[] embedding)> vectors,
        string                                           contentHash,
        CancellationToken                                cancellationToken = default
    );

    Task DeleteEmbeddingAsync(long projectID, long entryID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorSearchResult>> SearchByVectorAsync
    (
        long                 projectID,
        byte[]               queryVector,
        int                  topK,
        IReadOnlyList<long>? candidateIDs      = null,
        CancellationToken    cancellationToken = default
    );
}
