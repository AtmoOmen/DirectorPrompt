using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class DirectiveRepository
(
    SQLiteDatabaseScheduler scheduler
) : IDirectiveRepository
{
    public Task<IReadOnlyList<ActiveDirective>> GetActiveAsync
    (
        long              sessionID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteReadAsync<IReadOnlyList<ActiveDirective>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<ActiveDirective>
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

                return rows.ToList();
            },
            cancellationToken: cancellationToken
        );

    public Task<ActiveDirective> AddAsync
    (
        ActiveDirective   directive,
        long              sessionID,
        long              roundID,
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
                                     type      = directive.Type,
                                     content   = directive.Content,
                                     ttl       = directive.TTL,
                                     createdAt = directive.CreatedAt
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

    public Task RemoveAsync(long id, long sessionID, long roundID, CancellationToken cancellationToken = default) =>
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
                    sessionID,
                    roundID,
                    "active_directives",
                    id,
                    "delete",
                    JsonSerializer.Serialize(oldRow, JsonOptions.Compact),
                    token
                );
                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public Task<IReadOnlyList<ActiveDirective>> DecrementTTLAsync
    (
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteReadAsync<IReadOnlyList<ActiveDirective>>
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var affectedIDs = (await connection.QueryAsync<long>
                                   (
                                       new CommandDefinition
                                       (
                                           """
                                           SELECT id FROM active_directives
                                           WHERE session_id = @sessionID AND ttl IS NOT NULL
                                           """,
                                           new { sessionID },
                                           transaction,
                                           cancellationToken: token
                                       )
                                   )).ToList();

                foreach (var id in affectedIDs)
                {
                    var oldDataJSON = await RowReader.ReadRowAsJSONAsync
                                      (
                                          connection,
                                          "SELECT * FROM active_directives WHERE id = @id",
                                          new { id },
                                          transaction,
                                          token
                                      );
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
                        sessionID,
                        roundID,
                        "active_directives",
                        id,
                        "update",
                        oldDataJSON,
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
                        sessionID,
                        roundID,
                        "active_directives",
                        (long)row.id,
                        "delete",
                        JsonSerializer.Serialize(row, JsonOptions.Compact),
                        token
                    );
                }

                var rows = await connection.QueryAsync<ActiveDirective>
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

                return rows.ToList();
            },
            cancellationToken: cancellationToken
        );
}
