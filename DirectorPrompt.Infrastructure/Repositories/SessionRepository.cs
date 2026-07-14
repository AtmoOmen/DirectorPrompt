using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class SessionRepository
(
    SqliteDatabaseScheduler scheduler
) : ISessionRepository
{
    public Task<Session?> GetByIDAsync(long id, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                var row = await connection.QueryFirstOrDefaultAsync<SessionRow>
                          (
                              new CommandDefinition
                              (
                                  "SELECT * FROM sessions WHERE id = @id",
                                  new { id },
                                  cancellationToken: token
                              )
                          );

                return row?.ToSession();
            },
            cancellationToken: cancellationToken
        );

    public Task<IReadOnlyList<Session>> GetByProjectAsync
    (
        long              projectID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync<IReadOnlyList<Session>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<SessionRow>
                           (
                               new CommandDefinition
                               (
                                   "SELECT * FROM sessions WHERE project_id = @projectID ORDER BY id DESC",
                                   new { projectID },
                                   cancellationToken: token
                               )
                           );

                return rows.Select(row => row.ToSession()).ToList();
            },
            cancellationToken: cancellationToken
        );

    public Task<Session> CreateAsync(Session session, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                var now = DateTime.UtcNow;
                var id = await connection.ExecuteScalarAsync<long>
                         (
                             new CommandDefinition
                             (
                                 """
                                 INSERT INTO sessions (project_id, title, created_at, updated_at)
                                 VALUES (@projectID, @title, @createdAt, @updatedAt);
                                 SELECT last_insert_rowid();
                                 """,
                                 new
                                 {
                                     projectID = session.ProjectID,
                                     title     = session.Title,
                                     createdAt = now.ToString("O"),
                                     updatedAt = now.ToString("O")
                                 },
                                 cancellationToken: token
                             )
                         );

                return session with { ID = id, CreatedAt = now, UpdatedAt = now };
            },
            cancellationToken: cancellationToken
        );

    public Task DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        """
                        DELETE FROM round_changes
                        WHERE round_id IN (SELECT id FROM rounds WHERE scene_id IN (SELECT id FROM scenes WHERE session_id = @id));

                        DELETE FROM character_relation_logs
                        WHERE relation_id IN (SELECT id FROM character_relations WHERE session_id = @id);

                        DELETE FROM character_state_values
                        WHERE character_id IN (SELECT id FROM characters WHERE session_id = @id);

                        DELETE FROM character_category_resolutions
                        WHERE character_id IN (SELECT id FROM characters WHERE session_id = @id);

                        DELETE FROM character_scene_presence
                        WHERE character_id IN (SELECT id FROM characters WHERE session_id = @id)
                           OR scene_id IN (SELECT id FROM scenes WHERE session_id = @id);

                        DELETE FROM character_relations WHERE session_id = @id;
                        DELETE FROM characters WHERE session_id = @id;
                        DELETE FROM memory_entries WHERE session_id = @id;
                        DELETE FROM active_directives WHERE session_id = @id;
                        DELETE FROM playthrough_events WHERE session_id = @id;
                        DELETE FROM state_change_logs WHERE session_id = @id;
                        DELETE FROM state_values WHERE session_id = @id;

                        DELETE FROM rounds
                        WHERE scene_id IN (SELECT id FROM scenes WHERE session_id = @id);

                        DELETE FROM scenes WHERE session_id = @id;
                        DELETE FROM sessions WHERE id = @id;
                        """,
                        new { id },
                        transaction,
                        cancellationToken: token
                    )
                );
                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public Task UpdateAsync(Session session, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        """
                        UPDATE sessions
                        SET title = @title,
                            updated_at = @updatedAt
                        WHERE id = @id
                        """,
                        new
                        {
                            id        = session.ID,
                            title     = session.Title,
                            updatedAt = DateTime.UtcNow.ToString("O")
                        },
                        cancellationToken: token
                    )
                );
            },
            cancellationToken: cancellationToken
        );

    private sealed class SessionRow
    {
        public long   ID         { get; set; }
        public long   Project_ID { get; set; }
        public string Title      { get; set; } = string.Empty;
        public string Created_At { get; set; } = string.Empty;
        public string Updated_At { get; set; } = string.Empty;

        public Session ToSession() =>
            new()
            {
                ID        = ID,
                ProjectID = Project_ID,
                Title     = Title,
                CreatedAt = DateTime.Parse(Created_At),
                UpdatedAt = DateTime.Parse(Updated_At)
            };
    }
}
