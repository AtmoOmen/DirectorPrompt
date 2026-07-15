using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Repositories;

public interface IDirectiveRepository
{
    Task<IReadOnlyList<ActiveDirective>> GetActiveAsync(long sessionID, CancellationToken cancellationToken = default);

    Task<ActiveDirective> AddAsync(ActiveDirective directive, long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task RemoveAsync(long id, long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActiveDirective>> DecrementTTLAsync(long sessionID, long roundID, CancellationToken cancellationToken = default);
}
