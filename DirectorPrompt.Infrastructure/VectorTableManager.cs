using Dapper;
using Microsoft.Data.Sqlite;
using Serilog;

namespace DirectorPrompt.Infrastructure;

public sealed class VectorTableManager
{
    private const int CHUNK_SIZE = 64;

    private readonly SQLiteDatabaseScheduler scheduler;

    public VectorTableManager(SQLiteDatabaseScheduler scheduler) =>
        this.scheduler = scheduler;

    public static string GetKnowledgeTableName(long projectID) => $"knowledge_vec_{projectID}";

    public static string GetMemoryTableName(long projectID) => $"memory_vec_{projectID}";

    public static string GetCharacterTableName(long projectID) => $"character_vec_{projectID}";

    public Task EnsureTableAsync(string tableName, int dimension, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            (connection, token) => EnsureTableAsync(connection, tableName, dimension, token),
            SQLiteWorkPriority.Maintenance,
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
            {
                await EnsureDeleteTriggerAsync(connection, tableName, cancellationToken);
                return;
            }

            Log.Warning("向量表 {Table} 结构变更, 重建表", tableName);

            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP TABLE IF EXISTS \"{tableName}\"";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await CreateVecTableAsync(connection, tableName, dimension, cancellationToken);
        await EnsureDeleteTriggerAsync(connection, tableName, cancellationToken);

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
            SQLiteWorkPriority.Maintenance,
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
            {
                await EnsureDeleteTriggerAsync(connection, tableName, cancellationToken);
                return;
            }

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
        await EnsureDeleteTriggerAsync(connection, tableName, cancellationToken);

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
            SQLiteWorkPriority.Maintenance,
            cancellationToken
        );

    public static async Task<bool> TableExistsAsync
    (
        SqliteConnection  connection,
        string            tableName,
        CancellationToken cancellationToken
    ) =>
        await GetDimensionAsync(connection, tableName, cancellationToken) is not null;

    private static async Task<int?> GetDimensionAsync
    (
        SqliteConnection   connection,
        string             tableName,
        CancellationToken  cancellationToken,
        SqliteTransaction? transaction = null
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
        SqliteConnection   connection,
        string             tableName,
        int                dimension,
        CancellationToken  cancellationToken,
        SqliteTransaction? transaction = null
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"CREATE VIRTUAL TABLE IF NOT EXISTS \"{tableName}\" USING vec0(id INTEGER PRIMARY KEY, entry_id INTEGER, source TEXT, session_id INTEGER partition key, timeline_pos INTEGER, searchable INTEGER, embedding FLOAT[{dimension}] distance_metric=cosine, chunk_size={CHUNK_SIZE})";
        await command.ExecuteNonQueryAsync(cancellationToken);

        Log.Information("创建多向量 vec0 虚拟表: {Table}, 维度={Dimension}", tableName, dimension);
    }

    private static async Task CreateVecTableAsync
    (
        SqliteConnection   connection,
        string             tableName,
        int                dimension,
        CancellationToken  cancellationToken,
        SqliteTransaction? transaction = null
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"CREATE VIRTUAL TABLE IF NOT EXISTS \"{tableName}\" USING vec0(entry_id INTEGER PRIMARY KEY, embedding FLOAT[{dimension}] distance_metric=cosine, chunk_size={CHUNK_SIZE})";
        await command.ExecuteNonQueryAsync(cancellationToken);

        Log.Information("创建 vec0 虚拟表: {Table}, 维度={Dimension}", tableName, dimension);
    }

    private static bool UsesCosineDistance(string createSQL) =>
        createSQL.Contains("distance_metric=cosine", StringComparison.OrdinalIgnoreCase);

    internal static async Task CompactAsync
    (
        SqliteConnection  connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken,
        long?             targetProjectID = null
    )
    {
        var tables = await connection.QueryAsync<(string Name, string SQL)>
                     (
                         new CommandDefinition
                         (
                             """
                             SELECT name, sql FROM sqlite_schema
                             WHERE type = 'table' AND sql LIKE 'CREATE VIRTUAL TABLE%USING vec0%'
                               AND (@targetProjectID IS NULL OR name IN
                                   ('knowledge_vec_' || @targetProjectID, 'memory_vec_' || @targetProjectID, 'character_vec_' || @targetProjectID));
                             """,
                             new { targetProjectID },
                             transaction,
                             cancellationToken: cancellationToken
                         )
                     );

        foreach (var (tableName, sql) in tables)
        {
            var separator = tableName.LastIndexOf('_');

            if (separator < 0 || !long.TryParse(tableName.AsSpan(separator + 1), out var projectID))
                continue;

            var sourceTable = tableName[..separator] switch
            {
                "knowledge_vec" => "knowledge_entries",
                "memory_vec"    => "memory_entries",
                "character_vec" => "characters",
                _               => null
            };

            if (sourceTable is null)
                continue;

            var projectExists = await connection.ExecuteScalarAsync<bool>
                                (
                                    new CommandDefinition
                                    (
                                        "SELECT EXISTS(SELECT 1 FROM projects WHERE id = @projectID)",
                                        new { projectID },
                                        transaction,
                                        cancellationToken: cancellationToken
                                    )
                                );

            await connection.ExecuteAsync
            (
                new CommandDefinition
                (
                    $"DROP TRIGGER IF EXISTS \"{tableName}_delete\";",
                    transaction: transaction,
                    cancellationToken: cancellationToken
                )
            );

            if (!projectExists)
            {
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        $"DROP TABLE \"{tableName}\"; DELETE FROM vector_tables WHERE table_name = @tableName;",
                        new { tableName },
                        transaction,
                        cancellationToken: cancellationToken
                    )
                );
                continue;
            }

            var dimension = await GetDimensionAsync(connection, tableName, cancellationToken, transaction) ??
                            throw new InvalidOperationException($"向量表 {tableName} 缺少维度记录");
            var isMultiVector = sql.Contains("source", StringComparison.OrdinalIgnoreCase);
            var columns = isMultiVector ?
                              "v.id, v.entry_id, v.source, v.session_id, v.timeline_pos, v.searchable, v.embedding" :
                              "v.entry_id, v.embedding";
            var sessionFilter = sourceTable == "knowledge_entries" ?
                                    "" :
                                    "AND EXISTS(SELECT 1 FROM sessions s WHERE s.id = e.session_id AND s.project_id = e.project_id)";

            await connection.ExecuteAsync
            (
                new CommandDefinition
                (
                    $"""
                     CREATE TEMP TABLE vector_compaction AS
                     SELECT {columns}
                     FROM "{tableName}" v
                     JOIN {sourceTable} e ON e.id = v.entry_id
                     WHERE e.project_id = @projectID {sessionFilter};
                     DROP TABLE "{tableName}";
                     """,
                    new { projectID },
                    transaction,
                    cancellationToken: cancellationToken
                )
            );

            if (isMultiVector)
                await CreateMultiVectorTableAsync(connection, tableName, dimension, cancellationToken, transaction);
            else
                await CreateVecTableAsync(connection, tableName, dimension, cancellationToken, transaction);

            await connection.ExecuteAsync
            (
                new CommandDefinition
                (
                    $"INSERT INTO \"{tableName}\" SELECT * FROM temp.vector_compaction; DROP TABLE temp.vector_compaction;",
                    transaction: transaction,
                    cancellationToken: cancellationToken
                )
            );
            await EnsureDeleteTriggerAsync(connection, tableName, cancellationToken, transaction);
        }
    }

    private static async Task EnsureDeleteTriggerAsync
    (
        SqliteConnection   connection,
        string             tableName,
        CancellationToken  cancellationToken,
        SqliteTransaction? transaction = null
    )
    {
        var separator = tableName.LastIndexOf('_');
        var projectID = long.Parse(tableName.AsSpan(separator + 1));
        var sourceTable = tableName[..separator] switch
        {
            "knowledge_vec" => "knowledge_entries",
            "memory_vec"    => "memory_entries",
            "character_vec" => "characters",
            _               => throw new ArgumentException("未知的向量表", nameof(tableName))
        };

        await connection.ExecuteAsync
        (
            new CommandDefinition
            (
                $"""
                 CREATE TRIGGER IF NOT EXISTS "{tableName}_delete"
                 AFTER DELETE ON {sourceTable}
                 WHEN old.project_id = {projectID}
                 BEGIN
                     DELETE FROM "{tableName}" WHERE entry_id = old.id;
                 END;
                 """,
                transaction: transaction,
                cancellationToken: cancellationToken
            )
        );
    }
}
