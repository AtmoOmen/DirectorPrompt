using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Services;

public interface ISystemStateTransformer
{
    Task ExecuteAsync
    (
        long              projectID,
        long              sessionID,
        long?             sceneID,
        long              roundID,
        SystemTrigger     trigger,
        CancellationToken cancellationToken = default
    );

    IReadOnlyList<long> ConsumeInjectedKnowledge(long sessionID);
}
