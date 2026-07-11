using Microsoft.Data.Sqlite;
using Serilog;

namespace DirectorPrompt.Infrastructure;

public sealed class VectorTableManager
{
    private readonly SqliteConnectionFactory connectionFactory;

    public VectorTableManager(SqliteConnectionFactory connectionFactory) =>
        this.connectionFactory = connectionFactory;

    public static string GetKnowledgeTableName(long projectID) => $"knowledge_vec_{projectID}";

    public static string GetMemoryTableName(long projectID) => $"memory_vec_{projectID}";

    public static string GetCharacterTableName(long projectID) => $"character_vec_{projectID}";

    public async Task EnsureTableAsync(string tableName, int dimension, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var existingDimension = await GetDimensionAsync(connection, tableName, cancellationToken);

        if (existingDimension is not null)
        {
            if (existingDimension == dimension)
                return;

            Log.Warning
            (
                "向量表 {Table} 维度变更: {Old} -> {New}, 重建表",
                tableName,
                existingDimension,
                dimension
            );

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

    public async Task EnsureMultiVectorTableAsync(string tableName, int dimension, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var existingSQL = await GetCreateSQLAsync(connection, tableName, cancellationToken);

        if (existingSQL is not null && existingSQL.Contains("source", StringComparison.OrdinalIgnoreCase))
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
            Log.Warning("向量表 {Table} 升级为多向量结构, 重建", tableName);

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

    public async Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);
        return await GetDimensionAsync(connection, tableName, cancellationToken) is not null;
    }

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
        command.CommandText = $"CREATE VIRTUAL TABLE IF NOT EXISTS \"{tableName}\" USING vec0(id INTEGER PRIMARY KEY, entry_id INTEGER, source TEXT, embedding FLOAT[{dimension}])";
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
        command.CommandText = $"CREATE VIRTUAL TABLE IF NOT EXISTS \"{tableName}\" USING vec0(entry_id INTEGER PRIMARY KEY, embedding FLOAT[{dimension}])";
        await command.ExecuteNonQueryAsync(cancellationToken);

        Log.Information("创建 vec0 虚拟表: {Table}, 维度={Dimension}", tableName, dimension);
    }
}
