using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Infrastructure;
using DirectorPrompt.Infrastructure.Repositories;
using Xunit;

namespace DirectorPrompt.Tests;

public sealed class RepositoryTransactionTests
{
    [Fact]
    public async Task MemoryWriteRollsBackWhenAuditInsertFails()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        await context.Scheduler.ExecuteAsync
        (async (connection, token) =>
            {
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        """
                        INSERT INTO scenes
                        (id, project_id, session_id, timeline_position, time_label, status)
                        VALUES (1, 1, 1, 1000, 'test', 'active');

                        CREATE TRIGGER fail_round_audit
                        BEFORE INSERT ON round_changes
                        BEGIN
                            SELECT RAISE(ABORT, 'audit failure');
                        END;
                        """,
                        cancellationToken: token
                    )
                );
            }
        );
        var repository = new MemoryRepository(context.Scheduler);

        await Assert.ThrowsAnyAsync<Exception>
        (() => repository.CreateAsync
         (
             new MemoryEntry
             {
                 ProjectID   = 1,
                 SessionID   = 1,
                 SceneID     = 1,
                 TimelinePos = 1000,
                 Content     = "must rollback"
             },
             1,
             10
         )
        );
        var count = await context.Scheduler.ExecuteAsync
                    (async (connection, token) =>
                         await connection.ExecuteScalarAsync<long>
                         (
                             new CommandDefinition
                             (
                                 "SELECT COUNT(*) FROM memory_entries",
                                 cancellationToken: token
                             )
                         )
                    );

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CharacterWriteRollsBackWhenAuditInsertFails()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        await context.Scheduler.ExecuteAsync
        (async (connection, token) =>
            {
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        """
                        CREATE TRIGGER fail_character_audit
                        BEFORE INSERT ON round_changes
                        BEGIN
                            SELECT RAISE(ABORT, 'audit failure');
                        END;
                        """,
                        cancellationToken: token
                    )
                );
            }
        );
        var repository = new CharacterRepository
        (
            context.ConnectionFactory,
            new VectorTableManager(context.Scheduler),
            context.Scheduler
        );

        await Assert.ThrowsAnyAsync<Exception>
        (() => repository.CreateAsync
         (
             new Character
             {
                 ProjectID = 1,
                 SessionID = 1,
                 Name      = "must rollback"
             },
             1,
             10
         )
        );
        var count = await context.Scheduler.ExecuteAsync
                    (async (connection, token) =>
                         await connection.ExecuteScalarAsync<long>
                         (
                             new CommandDefinition
                             (
                                 "SELECT COUNT(*) FROM characters",
                                 cancellationToken: token
                             )
                         )
                    );

        Assert.Equal(0, count);
    }
}
