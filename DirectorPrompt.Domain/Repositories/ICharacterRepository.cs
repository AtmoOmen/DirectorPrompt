using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Repositories;

public interface ICharacterRepository
{
    Task<Character?> GetByIDAsync(long id, CancellationToken cancellationToken = default);

    Task<Character?> GetByNameAsync(long sessionID, string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Character>> GetBySessionAsync(long sessionID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Character>> GetByProjectAsync(long projectID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Character>> GetBySceneAsync(long sceneID, CancellationToken cancellationToken = default);

    Task<Character> CreateAsync(Character character, CancellationToken cancellationToken = default);

    Task UpdateAsync(Character character, CancellationToken cancellationToken = default);

    Task SetStatusAsync(long characterID, CharacterStatus status, CancellationToken cancellationToken = default);

    Task UpdateDirectivesAsync
    (
        long                          characterID,
        IReadOnlyList<DirectiveConfig> enterDirectives,
        IReadOnlyList<DirectiveConfig> exitDirectives,
        CancellationToken              cancellationToken = default
    );

    Task<IReadOnlyList<CharacterCategory>> GetCategoriesAsync(long projectID, CancellationToken cancellationToken = default);

    Task<CharacterCategory> CreateCategoryAsync(CharacterCategory category, CancellationToken cancellationToken = default);

    Task UpdateCategoryAsync(CharacterCategory category, CancellationToken cancellationToken = default);

    Task DeleteCategoryAsync(long categoryID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Character>> GetCharactersByCategoryAsync(long projectID, long categoryID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterRelation>> GetRelationsAsync(long sessionID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterRelation>> GetRelationsByCharacterAsync(long characterID, CancellationToken cancellationToken = default);

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
        CancellationToken    cancellationToken = default
    );

    Task<IReadOnlyList<CharacterRelationLog>> GetRelationLogsAsync(long relationID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterScenePresence>> GetPresenceAsync(long sceneID, CancellationToken cancellationToken = default);

    Task EnterSceneAsync(long characterID, long sceneID, CancellationToken cancellationToken = default);

    Task LeaveSceneAsync(long characterID, long sceneID, CancellationToken cancellationToken = default);

    Task<CharacterCategoryResolution?> GetResolvedCategoriesAsync(long characterID, CancellationToken cancellationToken = default);

    Task UpdateResolvedCategoriesAsync(CharacterCategoryResolution resolved, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterStateValue>> GetCharacterStateValuesAsync(long characterID, CancellationToken cancellationToken = default);

    Task SetCharacterStateValueAsync(long characterID, long attributeID, string value, CancellationToken cancellationToken = default);

    Task CloneProjectCharactersToSessionAsync(long projectID, long sessionID, CancellationToken cancellationToken = default);
}
