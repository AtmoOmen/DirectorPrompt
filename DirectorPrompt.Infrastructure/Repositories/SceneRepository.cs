using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class SceneRepository
(
    SQLiteDatabaseScheduler scheduler
) : ISceneRepository
{
    public Task<Scene?> GetByIDAsync(long id, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteReadAsync
        (
            async (connection, token) =>
                await connection.QueryFirstOrDefaultAsync<Scene>
                (
                    new CommandDefinition
                    (
                        "SELECT * FROM scenes WHERE id = @id",
                        new { id },
                        cancellationToken: token
                    )
                ),
            cancellationToken
        );

    public Task<IReadOnlyList<Scene>> GetBySessionAsync
    (
        long              sessionID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteReadAsync<IReadOnlyList<Scene>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<Scene>
                           (
                               new CommandDefinition
                               (
                                   "SELECT * FROM scenes WHERE session_id = @sessionID ORDER BY id",
                                   new { sessionID },
                                   cancellationToken: token
                               )
                           );

                return rows.ToList();
            },
            cancellationToken
        );

    public Task<Scene?> GetActiveSceneAsync(long sessionID, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteReadAsync
        (
            async (connection, token) =>
                await connection.QueryFirstOrDefaultAsync<Scene>
                (
                    new CommandDefinition
                    (
                        "SELECT * FROM scenes WHERE session_id = @sessionID AND status = 'Active' ORDER BY id DESC LIMIT 1",
                        new { sessionID },
                        cancellationToken: token
                    )
                ),
            cancellationToken
        );

    public Task<IReadOnlyList<Scene>> GetOrderedByTimelineAsync
    (
        long              sessionID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteReadAsync<IReadOnlyList<Scene>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<Scene>
                           (
                               new CommandDefinition
                               (
                                   "SELECT * FROM scenes WHERE session_id = @sessionID ORDER BY timeline_position",
                                   new { sessionID },
                                   cancellationToken: token
                               )
                           );

                return rows.ToList();
            },
            cancellationToken
        );

    public Task<Scene> CreateAsync(Scene scene, long sessionID, long roundID, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var id = await connection.ExecuteScalarAsync<long>
                         (
                             new CommandDefinition
                             (
                                 """
                                 INSERT INTO scenes (project_id, session_id, timeline_position, time_label, summary, progress_summary, progress_summary_round_id, status)
                                 VALUES (@projectID, @sessionID, @timelinePosition, @timeLabel, @summary, @progressSummary, @progressSummaryRoundID, @status);
                                 SELECT last_insert_rowid();
                                 """,
                                 new
                                 {
                                     projectID              = scene.ProjectID,
                                     sessionID              = scene.SessionID,
                                     timelinePosition       = scene.TimelinePosition,
                                     timeLabel              = scene.TimeLabel,
                                     summary                = scene.Summary,
                                     progressSummary        = scene.ProgressSummary,
                                     progressSummaryRoundID = scene.ProgressSummaryRoundID,
                                     status                 = scene.Status.ToString()
                                 },
                                 transaction,
                                 cancellationToken: token
                             )
                         );
                await RoundChangeRepository.RecordAsync
                (
                    connection,
                    transaction,
                    sessionID,
                    roundID,
                    "scenes",
                    id,
                    "create",
                    null,
                    token
                );
                await transaction.CommitAsync(token);

                return scene with { ID = id };
            },
            cancellationToken: cancellationToken
        );

    public Task UpdateAsync(Scene scene, long sessionID, long roundID, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var oldRow = await RowReader.ReadRowAsync
                             (
                                 connection,
                                 "SELECT * FROM scenes WHERE id = @id",
                                 new { id = scene.ID },
                                 transaction,
                                 token
                             );

                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        """
                        UPDATE scenes
                        SET time_label = @timeLabel,
                            summary = @summary,
                            progress_summary = @progressSummary,
                            progress_summary_round_id = @progressSummaryRoundID,
                            status = @status
                        WHERE id = @id
                        """,
                        new
                        {
                            id                     = scene.ID,
                            timeLabel              = scene.TimeLabel,
                            summary                = scene.Summary,
                            progressSummary        = scene.ProgressSummary,
                            progressSummaryRoundID = scene.ProgressSummaryRoundID,
                            status                 = scene.Status.ToString()
                        },
                        transaction,
                        cancellationToken: token
                    )
                );

                if (oldRow is not null)
                {
                    await RoundChangeRepository.RecordAsync
                    (
                        connection,
                        transaction,
                        sessionID,
                        roundID,
                        "scenes",
                        scene.ID,
                        "update",
                        JsonSerializer.Serialize(oldRow, JsonOptions.Compact),
                        token
                    );
                }

                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public Task UpdateProgressSummaryAsync
    (
        long              sceneID,
        string            progressSummary,
        long              throughRoundID,
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var oldRow = await RowReader.ReadRowAsync
                             (
                                 connection,
                                 "SELECT * FROM scenes WHERE id = @sceneID",
                                 new { sceneID },
                                 transaction,
                                 token
                             );
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        """
                        UPDATE scenes
                        SET progress_summary = @progressSummary,
                            progress_summary_round_id = @throughRoundID
                        WHERE id = @sceneID
                        """,
                        new { sceneID, progressSummary, throughRoundID },
                        transaction,
                        cancellationToken: token
                    )
                );

                if (oldRow is not null)
                {
                    await RoundChangeRepository.RecordAsync
                    (
                        connection,
                        transaction,
                        sessionID,
                        roundID,
                        "scenes",
                        sceneID,
                        "update",
                        JsonSerializer.Serialize(oldRow, JsonOptions.Compact),
                        token
                    );
                }

                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public Task CloseActiveSceneAsync
    (
        long              sessionID,
        long              roundID,
        string?           summary,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var oldRow = await RowReader.ReadRowAsync
                             (
                                 connection,
                                 "SELECT * FROM scenes WHERE session_id = @sessionID AND status = 'Active' ORDER BY id DESC LIMIT 1",
                                 new { sessionID },
                                 transaction,
                                 token
                             );

                if (oldRow is null)
                    return;

                var sceneID = Convert.ToInt64(oldRow["id"]);
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        "UPDATE scenes SET status = 'Completed', summary = @summary WHERE id = @sceneID",
                        new { sceneID, summary },
                        transaction,
                        cancellationToken: token
                    )
                );
                await RoundChangeRepository.RecordAsync
                (
                    connection,
                    transaction,
                    sessionID,
                    roundID,
                    "scenes",
                    sceneID,
                    "update",
                    JsonSerializer.Serialize(oldRow, JsonOptions.Compact),
                    token
                );
                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public Task<Scene?> GetLastCompletedSceneAsync
    (
        long              sessionID,
        long              beforeSceneID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteReadAsync
        (
            async (connection, token) => await connection.QueryFirstOrDefaultAsync<Scene>
                                         (
                                             new CommandDefinition
                                             (
                                                 "SELECT * FROM scenes WHERE session_id = @sessionID AND status = 'Completed' AND id < @beforeSceneID AND summary IS NOT NULL ORDER BY id DESC LIMIT 1",
                                                 new { sessionID, beforeSceneID },
                                                 cancellationToken: token
                                             )
                                         ),
            cancellationToken
        );
}
