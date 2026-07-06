using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Repositories;

public interface IEventRepository
{
    Task<PlaythroughEvent> AppendAsync(PlaythroughEvent eventItem, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlaythroughEvent>> GetBySessionAsync(long sessionID, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlaythroughEvent>> GetByRoundAsync(long roundID, CancellationToken cancellationToken = default);

    Task RemoveByRoundAsync(long roundID, CancellationToken cancellationToken = default);

    Task<long> GetLatestRoundIDAsync(long sessionID, CancellationToken cancellationToken = default);

    Task UpdateEventDataAsync(long eventID, string data, CancellationToken cancellationToken = default);
}
