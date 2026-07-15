using BenchmarkDotNet.Attributes;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Infrastructure;
using DirectorPrompt.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;

namespace DirectorPrompt.Benchmarks;

[MemoryDiagnoser]
public class MemoryPagingBenchmarks
{
    private string                  databasePath    = string.Empty;
    private SQLiteDatabaseScheduler scheduler       = null!;
    private MemoryRepository        repository      = null!;
    private EventRepository         eventRepository = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        databasePath = Path.Combine(Path.GetTempPath(), $"directorprompt-benchmark-{Guid.NewGuid():N}.db");
        var factory = new SQLiteConnectionFactory($"Data Source={databasePath};Pooling=False");
        scheduler = new SQLiteDatabaseScheduler(factory);
        await new SchemaMigrator(scheduler).MigrateAsync();
        await scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                await using var command     = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                                      INSERT INTO projects (id, name, description, opening_message, created_at, updated_at)
                                      VALUES (1, 'benchmark', '', '', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                                      INSERT INTO sessions (id, project_id, title, created_at, updated_at)
                                      VALUES (1, 1, 'benchmark', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                                      INSERT INTO scenes (id, project_id, session_id, timeline_position, time_label, status)
                                      VALUES (1, 1, 1, 100000, 'benchmark', 'Active');
                                      """;
                await command.ExecuteNonQueryAsync(token);

                command.CommandText = """
                                      INSERT INTO playthrough_events
                                      (project_id, session_id, round_id, scene_id, type, data, created_at)
                                      VALUES
                                      (1, 1, $round, 1, $type, $data, '2026-01-01T00:00:00Z')
                                      """;
                var roundParameter = command.Parameters.Add("$round", SqliteType.Integer);
                var typeParameter  = command.Parameters.Add("$type",  SqliteType.Text);
                var dataParameter  = command.Parameters.Add("$data",  SqliteType.Text);

                for (var round = 1; round <= 2000; round++)
                {
                    roundParameter.Value = round;
                    typeParameter.Value  = "DirectorInput";
                    dataParameter.Value  = $"input-{round}";
                    await command.ExecuteNonQueryAsync(token);
                    typeParameter.Value = "NarrativeOutput";
                    dataParameter.Value = $"output-{round}";
                    await command.ExecuteNonQueryAsync(token);
                }

                command.Parameters.Clear();
                command.CommandText = """
                                      INSERT INTO knowledge_entries
                                      (project_id, remarks, content, keywords, active, created_at, updated_at)
                                      VALUES
                                      (1, $remarks, $content, '[]', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
                                      """;
                var remarksParameter          = command.Parameters.Add("$remarks", SqliteType.Text);
                var knowledgeContentParameter = command.Parameters.Add("$content", SqliteType.Text);

                for (var index = 1; index <= 10000; index++)
                {
                    remarksParameter.Value          = $"knowledge-{index}";
                    knowledgeContentParameter.Value = $"deterministic knowledge content {index}";
                    await command.ExecuteNonQueryAsync(token);
                }

                command.Parameters.Clear();
                command.CommandText = """
                                      INSERT INTO characters
                                      (project_id, session_id, name, description, aliases, category_ids, status, touch_count, last_touched_round, created_at, updated_at)
                                      VALUES
                                      (1, 1, $name, '', '[]', '[]', 'Active', 0, 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
                                      """;
                var nameParameter = command.Parameters.Add("$name", SqliteType.Text);

                for (var index = 1; index <= 1000; index++)
                {
                    nameParameter.Value = $"character-{index}";
                    await command.ExecuteNonQueryAsync(token);
                }

                command.Parameters.Clear();
                command.CommandText = """
                                      INSERT INTO memory_entries
                                      (project_id, session_id, scene_id, timeline_pos, content, tags, related_character_ids, created_at, updated_at)
                                      VALUES
                                      (1, 1, 1, $timeline, $content, $tags, '[]', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
                                      """;
                var timelineParameter = command.Parameters.Add("$timeline", SqliteType.Integer);
                var contentParameter  = command.Parameters.Add("$content",  SqliteType.Text);
                var tagsParameter     = command.Parameters.Add("$tags",     SqliteType.Text);

                for (var index = 1; index <= 50000; index++)
                {
                    timelineParameter.Value = index;
                    contentParameter.Value = index % 10 == 0 ?
                                                 $"crystal benchmark memory {index}" :
                                                 $"ordinary benchmark memory {index}";
                    tagsParameter.Value = index % 3 == 0 ?
                                              "[\"important\"]" :
                                              "[]";
                    await command.ExecuteNonQueryAsync(token);
                }

                await transaction.CommitAsync(token);
            },
            SQLiteWorkPriority.Maintenance
        );
        repository      = new MemoryRepository(scheduler);
        eventRepository = new EventRepository(scheduler);
    }

    [Benchmark(Baseline = true)]
    public Task<MemoryPage> LoadLatestPageAsync() =>
        repository.GetPageAsync(new MemoryPageQuery(1, long.MaxValue));

    [Benchmark]
    public Task<MemoryPage> SearchPageAsync() =>
        repository.GetPageAsync
        (
            new MemoryPageQuery(1, long.MaxValue, SearchText: "crystal", Tag: "important")
        );

    [Benchmark]
    public Task<DialogPage> LoadDialogPageAsync() =>
        eventRepository.GetDialogPageAsync(new DialogPageQuery(1));

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await scheduler.DisposeAsync();
        Delete(databasePath);
        Delete($"{databasePath}-wal");
        Delete($"{databasePath}-shm");
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
