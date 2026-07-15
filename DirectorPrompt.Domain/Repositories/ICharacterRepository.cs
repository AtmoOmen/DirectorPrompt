using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Repositories;

public interface ICharacterRepository
{
    Task<Character?> GetByIDAsync(long id, CancellationToken cancellationToken = default);

    Task<Character?> GetByNameAsync(long sessionID, string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Character>> GetBySessionAsync(long sessionID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Character>> GetActiveBySessionAsync(long sessionID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Character>> GetBySceneAsync(long sceneID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Character>> GetByIDsAsync
    (
        long                sessionID,
        IReadOnlyList<long> characterIDs,
        CancellationToken   cancellationToken = default
    );

    Task<CharacterPage> GetPageAsync(CharacterPageQuery query, CancellationToken cancellationToken = default);

    Task<Character> CreateAsync(Character character, long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task UpdateAsync(Character character, long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task TouchAsync(long characterID, long roundID, long sessionID, CancellationToken cancellationToken = default);

    Task ArchiveAsync(long characterID, long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task ArchiveStaleAsync(long sessionID, long currentRound, int threshold, CancellationToken cancellationToken = default);

    Task AddAliasAsync(long characterID, string alias, long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task SaveEmbeddingAsync(long projectID, long characterID, byte[] embedding, string contentHash, CancellationToken cancellationToken = default);

    Task DeleteEmbeddingAsync(long projectID, long characterID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(long characterID, float distance)>> SearchByVectorAsync
    (
        long                 projectID,
        byte[]               queryVector,
        int                  topK,
        IReadOnlyList<long>? candidateIDs      = null,
        CancellationToken    cancellationToken = default
    );

    Task<IReadOnlyList<CharacterCategory>> GetCategoriesAsync(long projectID, CancellationToken cancellationToken = default);

    Task<CharacterCategory> CreateCategoryAsync(CharacterCategory category, CancellationToken cancellationToken = default);

    Task UpdateCategoryAsync(CharacterCategory category, CancellationToken cancellationToken = default);

    Task DeleteCategoryAsync(long categoryID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterRelation>> GetRelationsAsync(long sessionID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterRelation>> GetRelationsByCharacterAsync(long characterID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterRelation>> GetRelationsByCharactersAsync(IReadOnlyList<long> characterIDs, CancellationToken cancellationToken = default);

    Task<CharacterRelation> SetRelationAsync
    (
        long                 sessionID,
        long                 sourceCharacterID,
        long                 targetCharacterID,
        string               relationType,
        string?              description,
        float?               intensity,
        RelationChangeSource source,
        string               reason,
        long                 sceneID,
        long                 roundID,
        CancellationToken    cancellationToken = default
    );

    Task<IReadOnlyList<CharacterRelationLog>> GetRelationLogsAsync(long relationID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterScenePresence>> GetPresenceAsync(long sceneID, CancellationToken cancellationToken = default);

    Task EnterSceneAsync(long characterID, long sceneID, long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task LeaveSceneAsync(long characterID, long sceneID, long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task<CharacterCategoryResolution?> GetResolvedCategoriesAsync(long characterID, CancellationToken cancellationToken = default);

    Task UpdateResolvedCategoriesAsync(CharacterCategoryResolution resolved, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterStateValue>> GetCharacterStateValuesAsync(long characterID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterStateValue>> GetCharacterStateValuesBatchAsync(IReadOnlyList<long> characterIDs, CancellationToken cancellationToken = default);

    Task SetCharacterStateValueAsync(long characterID, long attributeID, string value, long sessionID, long roundID, CancellationToken cancellationToken = default);
}
