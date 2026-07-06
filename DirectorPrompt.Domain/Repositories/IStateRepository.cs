using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Repositories;

public interface IStateRepository
{
    Task<StateAttribute?> GetAttributeAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StateAttribute>> GetAttributesAsync(long projectID, StateScope? scope = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StateAttribute>> GetAttributesByCategoryAsync(long categoryID, CancellationToken cancellationToken = default);

    Task<StateAttribute> CreateAttributeAsync(StateAttribute attribute, CancellationToken cancellationToken = default);

    Task UpdateAttributeAsync(StateAttribute attribute, CancellationToken cancellationToken = default);

    Task DeleteAttributeAsync(long id, CancellationToken cancellationToken = default);

    Task<StateValue?> GetStateValueAsync(long attributeID, long sessionID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StateValue>> GetAllStateValuesAsync(long projectID, long sessionID, CancellationToken cancellationToken = default);

    Task SetStateValueAsync
    (
        long              attributeID,
        long              sessionID,
        string            value,
        StateChangeSource source,
        string            reason,
        long              sceneID,
        long?             roundID,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<CompositeItem>> GetCompositeItemsAsync(long attributeID, long sessionID, CancellationToken cancellationToken = default);

    Task<CompositeItem> AddCompositeItemAsync(CompositeItem item, long sessionID, CancellationToken cancellationToken = default);

    Task<CompositeItem> UpdateCompositeItemAsync(long itemID, float? delta, float? current, string reason, CancellationToken cancellationToken = default);

    Task RemoveCompositeItemAsync(long itemID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StateChangeLog>> GetChangeLogsAsync(long attributeID, long? sceneID = null, CancellationToken cancellationToken = default);
}
