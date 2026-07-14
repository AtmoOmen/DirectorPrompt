using Microsoft.Data.Sqlite;
using Serilog;

namespace DirectorPrompt.Infrastructure;

public sealed class VectorTableManager
{
    private readonly SqliteDatabaseScheduler scheduler;

    public VectorTableManager(SqliteDatabaseScheduler scheduler) =>
        this.scheduler = scheduler;

    public static string GetKnowledgeTableName(long projectID) => $"knowledge_vec_{projectID}";

    public static string GetMemoryTableName(long projectID) => $"memory_vec_{projectID}";

    public static string GetCharacterTableName(long projectID) => $"character_vec_{projectID}";

    public Task EnsureTableAsync(string tableName, int dimension, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            (connection, token) => EnsureTableAsync(connection, tableName, dimension, token),
            SqliteWorkPriority.Maintenance,
            cancellationToken
        );

    internal static async Task EnsureTableAsync
    (
        SqliteConnection  connection,
        string            tableName,
        int               dimension,
        CancellationToken cancellationToken
    )
    {
        var existingSQL       = await GetCreateSQLAsync(connection, tableName, cancellationToken);
        var existingDimension = await GetDimensionAsync(connection, tableName, cancellationToken);

        if (existingSQL is not null)
        {
            if (existingDimension == dimension && UsesCosineDistance(existingSQL))
                return;

            Log.Warning("向量表 {Table} 结构变更, 重建表", tableName);

            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP TABLE IF EXISTS \"{tableName}\"";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await CreateVecTableAsync(connection, tableName, dimension, cancellationToken);

        var now = DateTime.UtcNow.ToString("O");

        await using var metaCommand = connection.CreateCommand();
        metaCommand.CommandText = """
                                  INSERT INTO vector_tables (table_name, dimension, created_at)
                                  VALUES ($tableName, $dimension, $createdAt)
                                  ON CONFLICT(table_name) DO UPDATE SET dimension = $dimension, created_at = $createdAt
                                  """;
        metaCommand.Parameters.AddWithValue("$tableName", tableName);
        metaCommand.Parameters.AddWithValue("$dimension", dimension);
        metaCommand.Parameters.AddWithValue("$createdAt", now);
        await metaCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task EnsureMultiVectorTableAsync(string tableName, int dimension, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            (connection, token) => EnsureMultiVectorTableAsync(connection, tableName, dimension, token),
            SqliteWorkPriority.Maintenance,
            cancellationToken
        );

    internal static async Task EnsureMultiVectorTableAsync
    (
        SqliteConnection  connection,
        string            tableName,
        int               dimension,
        CancellationToken cancellationToken
    )
    {
        var existingSQL = await GetCreateSQLAsync(connection, tableName, cancellationToken);

        if (existingSQL is not null                                                  &&
            existingSQL.Contains("source",       StringComparison.OrdinalIgnoreCase) &&
            existingSQL.Contains("session_id",   StringComparison.OrdinalIgnoreCase) &&
            existingSQL.Contains("timeline_pos", StringComparison.OrdinalIgnoreCase) &&
            existingSQL.Contains("searchable",   StringComparison.OrdinalIgnoreCase) &&
            UsesCosineDistance(existingSQL))
        {
            var existingDimension = await GetDimensionAsync(connection, tableName, cancellationToken);

            if (existingDimension == dimension)
                return;

            Log.Warning
            (
                "向量表 {Table} 维度变更: {Old} -> {New}, 重建表",
                tableName,
                existingDimension,
                dimension
            );

            await using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = $"DROP TABLE IF EXISTS \"{tableName}\"";
            await dropCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        else if (existingSQL is not null)
        {
            Log.Warning("向量表 {Table} 结构变更, 重建", tableName);

            await using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = $"DROP TABLE IF EXISTS \"{tableName}\"";
            await dropCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await CreateMultiVectorTableAsync(connection, tableName, dimension, cancellationToken);

        var now = DateTime.UtcNow.ToString("O");

        await using var metaCommand = connection.CreateCommand();
        metaCommand.CommandText = """
                                  INSERT INTO vector_tables (table_name, dimension, created_at)
                                  VALUES ($tableName, $dimension, $createdAt)
                                  ON CONFLICT(table_name) DO UPDATE SET dimension = $dimension, created_at = $createdAt
                                  """;
        metaCommand.Parameters.AddWithValue("$tableName", tableName);
        metaCommand.Parameters.AddWithValue("$dimension", dimension);
        metaCommand.Parameters.AddWithValue("$createdAt", now);
        await metaCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            (connection, token) => TableExistsAsync(connection, tableName, token),
            SqliteWorkPriority.Maintenance,
            cancellationToken
        );

    internal static async Task<bool> TableExistsAsync
    (
        SqliteConnection  connection,
        string            tableName,
        CancellationToken cancellationToken
    ) =>
        await GetDimensionAsync(connection, tableName, cancellationToken) is not null;

    private static async Task<int?> GetDimensionAsync
    (
        SqliteConnection  connection,
        string            tableName,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT dimension FROM vector_tables WHERE table_name = $tableName";
        command.Parameters.AddWithValue("$tableName", tableName);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is long dim ?
                   (int)dim :
                   null;
    }

    private static async Task<string?> GetCreateSQLAsync
    (
        SqliteConnection  connection,
        string            tableName,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=$tableName";
        command.Parameters.AddWithValue("$tableName", tableName);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result as string;
    }

    private static async Task CreateMultiVectorTableAsync
    (
        SqliteConnection  connection,
        string            tableName,
        int               dimension,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"CREATE VIRTUAL TABLE IF NOT EXISTS \"{tableName}\" USING vec0(id INTEGER PRIMARY KEY, entry_id INTEGER, source TEXT, session_id INTEGER partition key, timeline_pos INTEGER, searchable INTEGER, embedding FLOAT[{dimension}] distance_metric=cosine)";
        await command.ExecuteNonQueryAsync(cancellationToken);

        Log.Information("创建多向量 vec0 虚拟表: {Table}, 维度={Dimension}", tableName, dimension);
    }

    private static async Task CreateVecTableAsync
    (
        SqliteConnection  connection,
        string            tableName,
        int               dimension,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"CREATE VIRTUAL TABLE IF NOT EXISTS \"{tableName}\" USING vec0(entry_id INTEGER PRIMARY KEY, embedding FLOAT[{dimension}] distance_metric=cosine)";
        await command.ExecuteNonQueryAsync(cancellationToken);

        Log.Information("创建 vec0 虚拟表: {Table}, 维度={Dimension}", tableName, dimension);
    }

    private static bool UsesCosineDistance(string createSQL) =>
        createSQL.Contains("distance_metric=cosine", StringComparison.OrdinalIgnoreCase);
}
