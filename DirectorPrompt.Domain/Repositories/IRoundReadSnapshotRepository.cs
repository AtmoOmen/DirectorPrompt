using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Repositories;

public interface IRoundReadSnapshotRepository
{
    Task<RoundReadSnapshot> GetAsync
    (
        long              projectID,
        long              sessionID,
        long?             sceneID,
        CancellationToken cancellationToken = default
    );
}
