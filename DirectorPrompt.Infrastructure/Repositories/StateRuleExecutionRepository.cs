using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class StateRuleExecutionRepository
(
    SQLiteConnectionFactory connectionFactory
) : IStateRuleExecutionRepository
{
    public async Task<StateRuleExecution?> GetByEventAsync
    (
        long sessionID,
        long attributeID,
        long? characterID,
        string ruleID,
        string eventKey,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<StateRuleExecution>
               (
                   """
                   SELECT * FROM state_rule_executions
                   WHERE session_id = @sessionID
                     AND attribute_id = @attributeID
                     AND character_id = @characterID
                     AND rule_id = @ruleID
                     AND event_key = @eventKey
                   """,
                   new { sessionID, attributeID, characterID = characterID ?? 0, ruleID, eventKey }
               );
    }

    public async Task<StateRuleExecution?> GetLatestAsync
    (
        long sessionID,
        long attributeID,
        long? characterID,
        string ruleID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<StateRuleExecution>
               (
                   """
                   SELECT * FROM state_rule_executions
                   WHERE session_id = @sessionID
                     AND attribute_id = @attributeID
                     AND character_id = @characterID
                     AND rule_id = @ruleID
                   ORDER BY id DESC
                   LIMIT 1
                   """,
                   new { sessionID, attributeID, characterID = characterID ?? 0, ruleID }
               );
    }

    public async Task<bool> HasFiredAsync
    (
        long sessionID,
        long attributeID,
        long? characterID,
        string ruleID,
        long? roundID,
        long? sceneID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var count = await connection.ExecuteScalarAsync<long>
                    (
                        """
                        SELECT COUNT(*) FROM state_rule_executions
                        WHERE session_id = @sessionID
                          AND attribute_id = @attributeID
                          AND character_id = @characterID
                          AND rule_id = @ruleID
                          AND fired = 1
                          AND (@roundID IS NULL OR round_id = @roundID)
                          AND (@sceneID IS NULL OR scene_id = @sceneID)
                        """,
                        new
                        {
                            sessionID,
                            attributeID,
                            characterID = characterID ?? 0,
                            ruleID,
                            roundID,
                            sceneID
                        }
                    );

        return count > 0;
    }

    public async Task RecordAsync(StateRuleExecution execution, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        await connection.ExecuteAsync
        (
            """
            INSERT INTO state_rule_executions
            (session_id, round_id, scene_id, attribute_id, character_id, rule_id, event_key, condition_met, fired, created_at)
            VALUES
            (@sessionID, @roundID, @sceneID, @attributeID, @characterID, @ruleID, @eventKey, @conditionMet, @fired, @createdAt)
            ON CONFLICT(session_id, attribute_id, character_id, rule_id, event_key) DO NOTHING
            """,
            new
            {
                sessionID    = execution.SessionID,
                roundID      = execution.RoundID,
                sceneID      = execution.SceneID,
                attributeID  = execution.AttributeID,
                characterID  = execution.CharacterID,
                ruleID       = execution.RuleID,
                eventKey     = execution.EventKey,
                conditionMet = execution.ConditionMet,
                fired        = execution.Fired,
                createdAt    = DateTime.UtcNow
            }
        );
    }

    public async Task DeleteByRoundAsync(long sessionID, long roundID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);
        await connection.ExecuteAsync
        (
            "DELETE FROM state_rule_executions WHERE session_id = @sessionID AND round_id = @roundID",
            new { sessionID, roundID }
        );
    }
}
