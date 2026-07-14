using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Repositories;

public interface IEventRepository
{
    Task<PlaythroughEvent> AppendAsync(PlaythroughEvent eventItem, CancellationToken cancellationToken = default);

    Task AppendBatchAsync(IReadOnlyList<PlaythroughEvent> events, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlaythroughEvent>> GetByRoundAsync(long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task<DialogPage> GetDialogPageAsync(DialogPageQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlaythroughEvent>> GetRecentBySceneAsync
    (
        long              sessionID,
        long              sceneID,
        long              beforeRoundID,
        int               maxRounds,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<PlaythroughEvent>> GetSceneSummaryChunkAsync
    (
        long              sessionID,
        long              sceneID,
        long              afterRoundID,
        long              beforeRoundID,
        int               retainedRecentRounds,
        int               chunkSize,
        CancellationToken cancellationToken = default
    );

    Task<PlaythroughEvent?> GetLatestByTypeBeforeRoundAsync
    (
        long              sessionID,
        EventType         type,
        long              beforeRoundID,
        CancellationToken cancellationToken = default
    );

    Task RemoveByRoundAsync(long sessionID, long roundID, CancellationToken cancellationToken = default);

    Task<long> GetLatestRoundIDAsync(long sessionID, CancellationToken cancellationToken = default);

    Task UpdateEventDataAsync(long eventID, string data, CancellationToken cancellationToken = default);
}
