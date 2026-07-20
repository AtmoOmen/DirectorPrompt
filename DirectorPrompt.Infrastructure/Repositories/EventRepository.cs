using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class EventRepository
(
    SQLiteDatabaseScheduler scheduler
) : IEventRepository
{
    public Task<PlaythroughEvent> AppendAsync
    (
        PlaythroughEvent  eventItem,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                var id = await connection.ExecuteScalarAsync<long>
                         (
                             new CommandDefinition
                             (
                                 """
                                 INSERT INTO playthrough_events (project_id, session_id, round_id, scene_id, type, data, created_at)
                                 VALUES (@projectID, @sessionID, @roundID, @sceneID, @type, @data, @createdAt);
                                 SELECT last_insert_rowid();
                                 """,
                                 CreateParameters(eventItem),
                                 cancellationToken: token
                             )
                         );

                return eventItem with { ID = id };
            },
            cancellationToken: cancellationToken
        );

    public Task AppendBatchAsync
    (
        IReadOnlyList<PlaythroughEvent> events,
        CancellationToken               cancellationToken = default
    )
    {
        if (events.Count == 0)
            return Task.CompletedTask;

        return scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);

                foreach (var eventItem in events)
                {
                    await connection.ExecuteAsync
                    (
                        new CommandDefinition
                        (
                            """
                            INSERT INTO playthrough_events (project_id, session_id, round_id, scene_id, type, data, created_at)
                            VALUES (@projectID, @sessionID, @roundID, @sceneID, @type, @data, @createdAt)
                            """,
                            CreateParameters(eventItem),
                            transaction,
                            cancellationToken: token
                        )
                    );
                }

                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );
    }

    public Task<IReadOnlyList<PlaythroughEvent>> GetByRoundAsync
    (
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteReadAsync<IReadOnlyList<PlaythroughEvent>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<PlaythroughEvent>
                           (
                               new CommandDefinition
                               (
                                   "SELECT * FROM playthrough_events WHERE session_id = @sessionID AND round_id = @roundID ORDER BY id",
                                   new { sessionID, roundID },
                                   cancellationToken: token
                               )
                           );

                return rows.ToList();
            },
            cancellationToken
        );

    public Task<DialogPage> GetDialogPageAsync
    (
        DialogPageQuery   query,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteReadAsync
        (
            async (connection, token) =>
            {
                var pageSize = Math.Clamp(query.PageSize, 1, 100);
                var rows = await connection.QueryAsync<PlaythroughEvent>
                           (
                               new CommandDefinition
                               (
                                   """
                                   WITH selected_rounds AS
                                   (
                                       SELECT DISTINCT round_id
                                       FROM playthrough_events
                                       WHERE session_id = @sessionID
                                         AND round_id > 0
                                         AND (@beforeRoundID IS NULL OR round_id < @beforeRoundID)
                                         AND type IN ('DirectorInput', 'NarrativeOutput')
                                       ORDER BY round_id DESC
                                       LIMIT @pageSize
                                   ),
                                   ranked_events AS
                                   (
                                       SELECT pe.*,
                                              ROW_NUMBER() OVER
                                              (
                                                  PARTITION BY pe.round_id, pe.type
                                                  ORDER BY pe.id DESC
                                              ) AS row_number
                                       FROM playthrough_events pe
                                       JOIN selected_rounds sr ON sr.round_id = pe.round_id
                                       WHERE pe.session_id = @sessionID
                                         AND pe.type IN ('DirectorInput', 'NarrativeOutput')
                                   )
                                   SELECT id, project_id, session_id, round_id, scene_id, type, data, created_at
                                   FROM ranked_events
                                   WHERE row_number = 1
                                   ORDER BY round_id, id
                                   """,
                                   new
                                   {
                                       sessionID     = query.SessionID,
                                       beforeRoundID = query.BeforeRoundID,
                                       pageSize
                                   },
                                   cancellationToken: token
                               )
                           );

                var events = rows.ToList();
                long? oldestRoundID = events.Count == 0 ?
                                          null :
                                          events.Min(item => item.RoundID);
                var hasPrevious = oldestRoundID is not null &&
                                  await connection.ExecuteScalarAsync<long>
                                  (
                                      new CommandDefinition
                                      (
                                          """
                                          SELECT EXISTS
                                          (
                                              SELECT 1
                                              FROM playthrough_events
                                              WHERE session_id = @sessionID
                                                AND round_id > 0
                                                AND round_id < @oldestRoundID
                                                AND type IN ('DirectorInput', 'NarrativeOutput')
                                          )
                                          """,
                                          new { sessionID = query.SessionID, oldestRoundID },
                                          cancellationToken: token
                                      )
                                  ) !=
                                  0;

                return new DialogPage
                (
                    events,
                    hasPrevious ?
                        oldestRoundID :
                        null
                );
            },
            cancellationToken
        );

    public Task<IReadOnlyList<PlaythroughEvent>> GetRecentBySceneAsync
    (
        long              sessionID,
        long              sceneID,
        long              beforeRoundID,
        int               maxRounds,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteReadAsync<IReadOnlyList<PlaythroughEvent>>
        (
            async (connection, token) =>
            {
                var roundLimit = Math.Clamp(maxRounds, 1, 200);
                var rows = await connection.QueryAsync<PlaythroughEvent>
                           (
                               new CommandDefinition
                               (
                                   """
                                   WITH selected_rounds AS
                                   (
                                       SELECT DISTINCT round_id
                                       FROM playthrough_events
                                       WHERE session_id = @sessionID
                                         AND scene_id = @sceneID
                                         AND round_id < @beforeRoundID
                                         AND type IN ('DirectorInput', 'NarrativeOutput')
                                       ORDER BY round_id DESC
                                       LIMIT @roundLimit
                                   ),
                                   ranked_events AS
                                   (
                                       SELECT pe.*,
                                              ROW_NUMBER() OVER
                                              (
                                                  PARTITION BY pe.round_id, pe.type
                                                  ORDER BY pe.id DESC
                                              ) AS row_number
                                       FROM playthrough_events pe
                                       JOIN selected_rounds sr ON sr.round_id = pe.round_id
                                       WHERE pe.session_id = @sessionID
                                         AND pe.scene_id = @sceneID
                                         AND pe.type IN ('DirectorInput', 'NarrativeOutput')
                                   )
                                   SELECT id, project_id, session_id, round_id, scene_id, type, data, created_at
                                   FROM ranked_events
                                   WHERE row_number = 1
                                   ORDER BY round_id, id
                                   """,
                                   new { sessionID, sceneID, beforeRoundID, roundLimit },
                                   cancellationToken: token
                               )
                           );

                return rows.ToList();
            },
            cancellationToken
        );

    public Task<PlaythroughEvent?> GetLatestByTypeBeforeRoundAsync
    (
        long              sessionID,
        EventType         type,
        long              beforeRoundID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteReadAsync
        (
            async (connection, token) =>
                await connection.QueryFirstOrDefaultAsync<PlaythroughEvent>
                (
                    new CommandDefinition
                    (
                        """
                        SELECT *
                        FROM playthrough_events
                        WHERE session_id = @sessionID
                          AND type = @type
                          AND round_id < @beforeRoundID
                        ORDER BY round_id DESC, id DESC
                        LIMIT 1
                        """,
                        new { sessionID, type = type.ToString(), beforeRoundID },
                        cancellationToken: token
                    )
                ),
            cancellationToken
        );

    public Task<IReadOnlyList<PlaythroughEvent>> GetSceneSummaryChunkAsync
    (
        long              sessionID,
        long              sceneID,
        long              afterRoundID,
        long              beforeRoundID,
        int               retainedRecentRounds,
        int               chunkSize,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteReadAsync<IReadOnlyList<PlaythroughEvent>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<PlaythroughEvent>
                           (
                               new CommandDefinition
                               (
                                   """
                                   WITH uncovered_rounds AS
                                   (
                                       SELECT DISTINCT round_id
                                       FROM playthrough_events
                                       WHERE session_id = @sessionID
                                         AND scene_id = @sceneID
                                         AND round_id > @afterRoundID
                                         AND round_id < @beforeRoundID
                                         AND type IN ('DirectorInput', 'NarrativeOutput')
                                   ),
                                   summarizable_rounds AS
                                   (
                                       SELECT round_id
                                       FROM uncovered_rounds
                                       ORDER BY round_id DESC
                                       LIMIT -1 OFFSET @retainedRecentRounds
                                   ),
                                   selected_rounds AS
                                   (
                                       SELECT round_id
                                       FROM summarizable_rounds
                                       ORDER BY round_id
                                       LIMIT @chunkSize
                                   ),
                                   ranked_events AS
                                   (
                                       SELECT pe.*,
                                              ROW_NUMBER() OVER
                                              (
                                                  PARTITION BY pe.round_id, pe.type
                                                  ORDER BY pe.id DESC
                                              ) AS row_number
                                       FROM playthrough_events pe
                                       JOIN selected_rounds sr ON sr.round_id = pe.round_id
                                       WHERE pe.session_id = @sessionID
                                         AND pe.scene_id = @sceneID
                                         AND pe.type IN ('DirectorInput', 'NarrativeOutput')
                                   )
                                   SELECT id, project_id, session_id, round_id, scene_id, type, data, created_at
                                   FROM ranked_events
                                   WHERE row_number = 1
                                   ORDER BY round_id, id
                                   """,
                                   new
                                   {
                                       sessionID,
                                       sceneID,
                                       afterRoundID,
                                       beforeRoundID,
                                       retainedRecentRounds = Math.Clamp(retainedRecentRounds, 1, 200),
                                       chunkSize            = Math.Clamp(chunkSize,            1, 100)
                                   },
                                   cancellationToken: token
                               )
                           );

                return rows.ToList();
            },
            cancellationToken
        );

    public Task RemoveByRoundAsync
    (
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        "DELETE FROM playthrough_events WHERE session_id = @sessionID AND round_id = @roundID",
                        new { sessionID, roundID },
                        cancellationToken: token
                    )
                );
            },
            cancellationToken: cancellationToken
        );

    public Task<long> GetLatestRoundIDAsync(long sessionID, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteReadAsync
        (
            async (connection, token) =>
            {
                var result = await connection.QueryFirstOrDefaultAsync<long?>
                             (
                                 new CommandDefinition
                                 (
                                     "SELECT MAX(round_id) FROM playthrough_events WHERE session_id = @sessionID",
                                     new { sessionID },
                                     cancellationToken: token
                                 )
                             );

                return result ?? 0;
            },
            cancellationToken
        );

    public Task UpdateEventDataAsync
    (
        long              eventID,
        string            data,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        "UPDATE playthrough_events SET data = @data WHERE id = @eventID",
                        new { eventID, data },
                        cancellationToken: token
                    )
                );
            },
            cancellationToken: cancellationToken
        );

    private static object CreateParameters(PlaythroughEvent eventItem) =>
        new
        {
            projectID = eventItem.ProjectID,
            sessionID = eventItem.SessionID,
            roundID   = eventItem.RoundID,
            sceneID   = eventItem.SceneID,
            type      = eventItem.Type.ToString(),
            data      = eventItem.Data,
            createdAt = eventItem.CreatedAt.ToString("O")
        };
}
