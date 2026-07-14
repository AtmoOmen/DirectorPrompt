using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Repositories;

public interface ISceneRepository
{
    Task<Scene?> GetByIDAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Scene>> GetBySessionAsync(long sessionID, CancellationToken cancellationToken = default);

    Task<Scene?> GetActiveSceneAsync(long sessionID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Scene>> GetOrderedByTimelineAsync(long sessionID, CancellationToken cancellationToken = default);

    Task<Scene> CreateAsync(Scene scene, CancellationToken cancellationToken = default);

    Task UpdateAsync(Scene scene, CancellationToken cancellationToken = default);

    Task UpdateProgressSummaryAsync
    (
        long              sceneID,
        string            progressSummary,
        long              throughRoundID,
        CancellationToken cancellationToken = default
    );

    Task CloseActiveSceneAsync(long sessionID, string? summary, CancellationToken cancellationToken = default);

    Task<Scene?> GetLastCompletedSceneAsync(long sessionID, long beforeSceneID, CancellationToken cancellationToken = default);
}
