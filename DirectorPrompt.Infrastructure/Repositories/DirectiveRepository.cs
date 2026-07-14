using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class DirectiveRepository
(
    SqliteDatabaseScheduler scheduler
) : IDirectiveRepository
{
    public Task<IReadOnlyList<ActiveDirective>> GetActiveAsync
    (
        long              sessionID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync<IReadOnlyList<ActiveDirective>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<ActiveDirectiveRow>
                           (
                               new CommandDefinition
                               (
                                   """
                                   SELECT * FROM active_directives
                                   WHERE session_id = @sessionID
                                     AND (ttl IS NULL OR ttl > 0)
                                   ORDER BY id
                                   """,
                                   new { sessionID },
                                   cancellationToken: token
                               )
                           );

                return rows.Select(row => row.ToActiveDirective()).ToList();
            },
            cancellationToken: cancellationToken
        );

    public Task<ActiveDirective> AddAsync
    (
        ActiveDirective   directive,
        CancellationToken cancellationToken = default
    ) =>
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
                                 INSERT INTO active_directives (project_id, session_id, type, content, ttl, created_at)
                                 VALUES (@projectID, @sessionID, @type, @content, @ttl, @createdAt);
                                 SELECT last_insert_rowid();
                                 """,
                                 new
                                 {
                                     projectID = directive.ProjectID,
                                     sessionID = directive.SessionID,
                                     type      = JsonNamingPolicy.SnakeCaseLower.ConvertName(directive.Type.ToString()),
                                     content   = directive.Content,
                                     ttl       = directive.TTL,
                                     createdAt = directive.CreatedAt.ToString("O")
                                 },
                                 transaction,
                                 cancellationToken: token
                             )
                         );
                await RoundChangeRepository.RecordAsync
                (
                    connection,
                    transaction,
                    RoundContext.SessionID ?? directive.SessionID,
                    RoundContext.Current   ?? 0,
                    "active_directives",
                    id,
                    "create",
                    null,
                    token
                );
                await transaction.CommitAsync(token);

                return directive with { ID = id };
            },
            cancellationToken: cancellationToken
        );

    public Task RemoveAsync(long id, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var oldRow = await RowReader.ReadRowAsync
                             (
                                 connection,
                                 "SELECT * FROM active_directives WHERE id = @id",
                                 new { id },
                                 transaction,
                                 token
                             );

                if (oldRow is null)
                    return;

                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        "DELETE FROM active_directives WHERE id = @id",
                        new { id },
                        transaction,
                        cancellationToken: token
                    )
                );
                await RoundChangeRepository.RecordAsync
                (
                    connection,
                    transaction,
                    RoundContext.SessionID ?? Convert.ToInt64(oldRow["session_id"]),
                    RoundContext.Current   ?? 0,
                    "active_directives",
                    id,
                    "delete",
                    JsonSerializer.Serialize(oldRow),
                    token
                );
                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public Task<IReadOnlyList<ActiveDirective>> DecrementTTLAsync
    (
        long              sessionID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync<IReadOnlyList<ActiveDirective>>
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var affectedRows = (await connection.QueryAsync
                                    (
                                        new CommandDefinition
                                        (
                                            """
                                            SELECT id, ttl FROM active_directives
                                            WHERE session_id = @sessionID AND ttl IS NOT NULL
                                            """,
                                            new { sessionID },
                                            transaction,
                                            cancellationToken: token
                                        )
                                    )).ToList();
                var roundID        = RoundContext.Current   ?? 0;
                var auditSessionID = RoundContext.SessionID ?? sessionID;

                foreach (var row in affectedRows)
                {
                    var id     = (long)row.id;
                    var oldTTL = (long)row.ttl;
                    await connection.ExecuteAsync
                    (
                        new CommandDefinition
                        (
                            "UPDATE active_directives SET ttl = ttl - 1 WHERE id = @id",
                            new { id },
                            transaction,
                            cancellationToken: token
                        )
                    );
                    await RoundChangeRepository.RecordAsync
                    (
                        connection,
                        transaction,
                        auditSessionID,
                        roundID,
                        "active_directives",
                        id,
                        "update",
                        JsonSerializer.Serialize(new { id, ttl = oldTTL }),
                        token
                    );
                }

                var expiredRows = (await connection.QueryAsync
                                   (
                                       new CommandDefinition
                                       (
                                           """
                                           SELECT * FROM active_directives
                                           WHERE session_id = @sessionID AND ttl IS NOT NULL AND ttl <= 0
                                           """,
                                           new { sessionID },
                                           transaction,
                                           cancellationToken: token
                                       )
                                   )).ToList();
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        "DELETE FROM active_directives WHERE session_id = @sessionID AND ttl IS NOT NULL AND ttl <= 0",
                        new { sessionID },
                        transaction,
                        cancellationToken: token
                    )
                );

                foreach (var row in expiredRows)
                {
                    await RoundChangeRepository.RecordAsync
                    (
                        connection,
                        transaction,
                        auditSessionID,
                        roundID,
                        "active_directives",
                        (long)row.id,
                        "delete",
                        JsonSerializer.Serialize(row),
                        token
                    );
                }

                var rows = await connection.QueryAsync<ActiveDirectiveRow>
                           (
                               new CommandDefinition
                               (
                                   """
                                   SELECT * FROM active_directives
                                   WHERE session_id = @sessionID
                                     AND (ttl IS NULL OR ttl > 0)
                                   ORDER BY id
                                   """,
                                   new { sessionID },
                                   transaction,
                                   cancellationToken: token
                               )
                           );
                await transaction.CommitAsync(token);

                return rows.Select(row => row.ToActiveDirective()).ToList();
            },
            cancellationToken: cancellationToken
        );

    private sealed class ActiveDirectiveRow
    {
        public long   ID         { get; set; }
        public long   Project_ID { get; set; }
        public long?  Session_ID { get; set; }
        public string Type       { get; set; } = "plot";
        public string Content    { get; set; } = string.Empty;
        public int?   TTL        { get; set; }
        public string Created_At { get; set; } = string.Empty;

        public ActiveDirective ToActiveDirective() =>
            new()
            {
                ID        = ID,
                ProjectID = Project_ID,
                SessionID = Session_ID ?? 0,
                Type = Type switch
                {
                    "tone"                 => DirectiveType.Tone,
                    "temporary_constraint" => DirectiveType.TemporaryConstraint,
                    "scene_change"         => DirectiveType.SceneChange,
                    _                      => DirectiveType.Plot
                },
                Content   = Content,
                TTL       = TTL,
                CreatedAt = DateTime.Parse(Created_At)
            };
    }
}
