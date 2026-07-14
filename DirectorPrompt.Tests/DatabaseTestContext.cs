using DirectorPrompt.Infrastructure;

namespace DirectorPrompt.Tests;

public sealed class DatabaseTestContext : IAsyncDisposable
{
    private DatabaseTestContext
    (
        string                  databasePath,
        SqliteConnectionFactory connectionFactory,
        SqliteDatabaseScheduler scheduler
    )
    {
        DatabasePath      = databasePath;
        ConnectionFactory = connectionFactory;
        Scheduler         = scheduler;
    }

    public string DatabasePath { get; }

    public SqliteConnectionFactory ConnectionFactory { get; }

    public SqliteDatabaseScheduler Scheduler { get; }

    public static async Task<DatabaseTestContext> CreateAsync(bool enableVecExtension = true)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"directorprompt-{Guid.NewGuid():N}.db");
        var factory      = new SqliteConnectionFactory($"Data Source={databasePath};Pooling=False", enableVecExtension);
        var scheduler    = new SqliteDatabaseScheduler(factory);
        var context      = new DatabaseTestContext(databasePath, factory, scheduler);
        var migrator     = new SchemaMigrator(scheduler);
        await migrator.MigrateAsync();

        await scheduler.ExecuteAsync
        (async (connection, token) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
                                      INSERT INTO projects (id, name, description, opening_message, created_at, updated_at)
                                      VALUES (1, 'test', '', '', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                                      INSERT INTO sessions (id, project_id, title, created_at, updated_at)
                                      VALUES (1, 1, 'test', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                                      """;
                await command.ExecuteNonQueryAsync(token);
            }
        );

        return context;
    }

    public async ValueTask DisposeAsync()
    {
        await Scheduler.DisposeAsync();
        DeleteIfExists(DatabasePath);
        DeleteIfExists($"{DatabasePath}-wal");
        DeleteIfExists($"{DatabasePath}-shm");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
