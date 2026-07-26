using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Domain.Repositories;

public interface IStateRuleExecutionRepository
{
    Task<StateRuleExecution?> GetByEventAsync
    (
        long sessionID,
        long attributeID,
        long? characterID,
        string ruleID,
        string eventKey,
        CancellationToken cancellationToken = default
    );

    Task<StateRuleExecution?> GetLatestAsync
    (
        long sessionID,
        long attributeID,
        long? characterID,
        string ruleID,
        CancellationToken cancellationToken = default
    );

    Task<bool> HasFiredAsync
    (
        long sessionID,
        long attributeID,
        long? characterID,
        string ruleID,
        long? roundID,
        long? sceneID,
        CancellationToken cancellationToken = default
    );

    Task RecordAsync(StateRuleExecution execution, CancellationToken cancellationToken = default);

    Task DeleteByRoundAsync(long sessionID, long roundID, CancellationToken cancellationToken = default);
}
