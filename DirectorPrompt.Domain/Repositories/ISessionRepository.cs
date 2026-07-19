using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Repositories;

public interface ISessionRepository
{
    Task<Session?> GetByIDAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Session>> GetByProjectAsync(long projectID, CancellationToken cancellationToken = default);

    Task<Session> CreateAsync
    (
        Session                   session,
        IReadOnlyList<StateValue>? initialStateValues = null,
        CancellationToken         cancellationToken = default
    );

    Task UpdateAsync(Session session, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
