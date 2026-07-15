using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Repositories;

public interface ISceneRepository
{
    Task<Scene?> GetByIDAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Scene>> GetBySessionAsync(long sessionID, CancellationToken cancellationToken = default);

    Task<Scene?> GetActiveSceneAsync(long sessionID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Scene>> GetOrderedByTimelineAsync(long sessionID, CancellationToken cancellationToken = default);

    Task<Scene> CreateAsync(Scene scene, long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task UpdateAsync(Scene scene, long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task UpdateProgressSummaryAsync
    (
        long              sceneID,
        string            progressSummary,
        long              throughRoundID,
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken = default
    );

    Task CloseActiveSceneAsync(long sessionID, long roundID, string? summary, CancellationToken cancellationToken = default);

    Task<Scene?> GetLastCompletedSceneAsync(long sessionID, long beforeSceneID, CancellationToken cancellationToken = default);
}
