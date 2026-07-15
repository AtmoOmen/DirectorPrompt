using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Services;

public interface ICharacterCategoryResolver
{
    Task<CharacterCategoryResolution?> ResolveAsync(long characterID, CancellationToken cancellationToken = default);

    Task<CharacterCategoryResolution?> ResolveAndPersistAsync(long characterID, long sessionID, long roundID, CancellationToken cancellationToken = default);
}
