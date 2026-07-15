using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Infrastructure;
using DirectorPrompt.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DirectorPrompt.Tests;

public sealed class RepositoryPagingTests
{
    [Fact]
    public async Task DialogPagesUseStableRoundCursor()
    {
        await using var context    = await DatabaseTestContext.CreateAsync();
        var             repository = new EventRepository(context.Scheduler);

        for (var roundID = 1; roundID <= 50; roundID++)
            await repository.AppendBatchAsync
            (
                [
                    CreateEvent(roundID, EventType.DirectorInput,   $"[{{\"type\":\"Plot\",\"content\":\"input-{roundID}\",\"order\":1}}]"),
                    CreateEvent(roundID, EventType.NarrativeOutput, $"output-{roundID}")
                ]
            );

        var firstPage  = await repository.GetDialogPageAsync(new DialogPageQuery(1));
        var secondPage = await repository.GetDialogPageAsync(new DialogPageQuery(1, firstPage.PreviousRoundID));

        Assert.Equal(80, firstPage.Events.Count);
        Assert.Equal(20, secondPage.Events.Count);
        Assert.Equal(11, firstPage.Events.Min(item => item.RoundID));
        Assert.Equal(1,  secondPage.Events.Min(item => item.RoundID));
        Assert.Null(secondPage.PreviousRoundID);
    }

    [Fact]
    public async Task MemoryPagesUseSearchAndCompositeCursor()
    {
        await using var context = await DatabaseTestContext.CreateAsync();

        await context.Scheduler.ExecuteAsync
        (async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);

                for (var index = 1; index <= 150; index++)
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = (SqliteTransaction)transaction;
                    command.CommandText = """
                                          INSERT INTO memory_entries
                                          (project_id, session_id, scene_id, timeline_pos, content, tags, related_character_ids, created_at, updated_at)
                                          VALUES
                                          (1, 1, 1, $timeline, $content, $tags, '[]', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
                                          """;
                    command.Parameters.AddWithValue("$timeline", index);
                    command.Parameters.AddWithValue
                    (
                        "$content",
                        index % 2 == 0 ?
                            $"crystal memory {index}" :
                            $"ordinary memory {index}"
                    );
                    command.Parameters.AddWithValue
                    (
                        "$tags",
                        index % 3 == 0 ?
                            "[\"important\"]" :
                            "[]"
                    );
                    await command.ExecuteNonQueryAsync(token);
                }

                await transaction.CommitAsync(token);
            }
        );

        var repository = new MemoryRepository(context.Scheduler);
        var firstPage  = await repository.GetPageAsync(new MemoryPageQuery(1, long.MaxValue));
        var secondPage = await repository.GetPageAsync
                         (
                             new MemoryPageQuery
                             (
                                 1,
                                 long.MaxValue,
                                 firstPage.NextTimelinePosition,
                                 firstPage.NextID
                             )
                         );
        var filtered = await repository.GetPageAsync
                       (
                           new MemoryPageQuery(1, long.MaxValue, SearchText: "crystal", Tag: "important")
                       );

        Assert.Equal(100, firstPage.Items.Count);
        Assert.Equal(50,  secondPage.Items.Count);
        Assert.Empty(firstPage.Items.Select(item => item.ID).Intersect(secondPage.Items.Select(item => item.ID)));
        Assert.All(filtered.Items, item => Assert.Contains("crystal",   item.Content));
        Assert.All(filtered.Items, item => Assert.Contains("important", item.Tags));
    }

    [Fact]
    public async Task CharacterPagesUseStableIDCursor()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        await context.Scheduler.ExecuteAsync
        (async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                await using var command     = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                                      INSERT INTO characters
                                      (project_id, session_id, name, description, aliases, category_ids, status, touch_count, last_touched_round, created_at, updated_at)
                                      VALUES
                                      (1, 1, $name, '', '[]', '[]', 'Active', 0, 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
                                      """;
                var nameParameter = command.Parameters.Add("$name", SqliteType.Text);

                for (var index = 1; index <= 250; index++)
                {
                    nameParameter.Value = $"character-{index:D3}";
                    await command.ExecuteNonQueryAsync(token);
                }

                await transaction.CommitAsync(token);
            }
        );
        var repository = new CharacterRepository
        (
            context.ConnectionFactory,
            new VectorTableManager(context.Scheduler),
            context.Scheduler
        );

        var first  = await repository.GetPageAsync(new CharacterPageQuery(1));
        var second = await repository.GetPageAsync(new CharacterPageQuery(1, first.NextID));
        var third  = await repository.GetPageAsync(new CharacterPageQuery(1, second.NextID));

        Assert.Equal(100, first.Items.Count);
        Assert.Equal(100, second.Items.Count);
        Assert.Equal(50,  third.Items.Count);
        Assert.Empty(first.Items.Select(item => item.ID).Intersect(second.Items.Select(item => item.ID)));
        Assert.Empty(second.Items.Select(item => item.ID).Intersect(third.Items.Select(item => item.ID)));
        Assert.Null(third.NextID);
    }

    [Fact]
    public async Task MemorySearchIndexTracksInsertUpdateAndDelete()
    {
        await using var context = await DatabaseTestContext.CreateAsync();

        async Task<long> CountMatchesAsync() =>
            await context.Scheduler.ExecuteAsync
            (async (connection, token) =>
                 await connection.ExecuteScalarAsync<long>
                 (
                     new CommandDefinition
                     (
                         "SELECT COUNT(*) FROM memory_search WHERE memory_search MATCH '\"crystal\"'",
                         cancellationToken: token
                     )
                 )
            );

        await context.Scheduler.ExecuteAsync
        (async (connection, token) =>
            {
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        """
                        INSERT INTO memory_entries
                        (id, project_id, session_id, scene_id, timeline_pos, content, tags, related_character_ids, created_at, updated_at)
                        VALUES
                        (1, 1, 1, 1, 1, 'crystal signal', '[]', '[]', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
                        """,
                        cancellationToken: token
                    )
                );
            }
        );
        Assert.Equal(1, await CountMatchesAsync());

        await context.Scheduler.ExecuteAsync
        (async (connection, token) =>
            {
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        "UPDATE memory_entries SET content = 'ordinary signal' WHERE id = 1",
                        cancellationToken: token
                    )
                );
            }
        );
        Assert.Equal(0, await CountMatchesAsync());

        await context.Scheduler.ExecuteAsync
        (async (connection, token) =>
            {
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        "DELETE FROM memory_entries WHERE id = 1",
                        cancellationToken: token
                    )
                );
            }
        );
        Assert.Equal(0, await CountMatchesAsync());
    }

    [Fact]
    public async Task VecTableFiltersBySessionPartition()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var             manager = new VectorTableManager(context.Scheduler);
        await manager.EnsureMultiVectorTableAsync("test_memory_vec", 3);

        await using var connection = await context.ConnectionFactory.CreateAsync(loadVectorExtension: true);
        var             first      = ToBytes([1f, 0f, 0f]);
        var             second     = ToBytes([0f, 1f, 0f]);
        await connection.ExecuteAsync
        (
            """
            INSERT INTO test_memory_vec (entry_id, source, session_id, timeline_pos, searchable, embedding)
            VALUES (@entryID, 'content', @sessionID, 1, 1, @embedding)
            """,
            new[]
            {
                new { entryID = 1, sessionID = 1, embedding = first },
                new { entryID = 2, sessionID = 2, embedding = second }
            }
        );
        var result = await connection.QuerySingleAsync<long>
                     (
                         """
                         SELECT entry_id
                         FROM test_memory_vec
                         WHERE embedding MATCH @query
                           AND k = 10
                           AND session_id = 1
                         ORDER BY distance
                         """,
                         new { query = first }
                     );

        Assert.Equal(1, result);
    }

    private static PlaythroughEvent CreateEvent(long roundID, EventType type, string data) =>
        new()
        {
            ProjectID = 1,
            SessionID = 1,
            RoundID   = roundID,
            Type      = type,
            Data      = data,
            CreatedAt = DateTime.UtcNow
        };

    private static byte[] ToBytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
