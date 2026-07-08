using System.Data.Common;
using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Serilog;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class RoundChangeRepository : IRoundChangeRepository
{
    private static readonly HashSet<string> ValidTables =
    [
        "memory_entries",
        "characters",
        "character_relations",
        "character_state_values",
        "character_scene_presence",
        "active_directives",
        "scenes"
    ];

    private static readonly Dictionary<string, string[]> CompositeKeys = new()
    {
        ["character_state_values"]   = ["character_id", "attribute_id"],
        ["character_scene_presence"] = ["character_id", "scene_id"]
    };

    private readonly SqliteConnectionFactory connectionFactory;

    public RoundChangeRepository(SqliteConnectionFactory connectionFactory) =>
        this.connectionFactory = connectionFactory;

    public async Task RecordCreateAsync
    (
        long              roundID,
        string            tableName,
        long              recordID,
        string?           oldDataJSON       = null,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            """
            INSERT INTO round_changes (round_id, table_name, record_id, operation, old_data, created_at)
            VALUES (@roundID, @tableName, @recordID, 'create', @oldData, @createdAt)
            """,
            new
            {
                roundID,
                tableName,
                recordID,
                oldData   = oldDataJSON,
                createdAt = DateTime.UtcNow.ToString("O")
            }
        );
    }

    public async Task RecordUpdateAsync
    (
        long              roundID,
        string            tableName,
        long              recordID,
        string            oldDataJSON,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            """
            INSERT INTO round_changes (round_id, table_name, record_id, operation, old_data, created_at)
            VALUES (@roundID, @tableName, @recordID, 'update', @oldData, @createdAt)
            """,
            new
            {
                roundID,
                tableName,
                recordID,
                oldData   = oldDataJSON,
                createdAt = DateTime.UtcNow.ToString("O")
            }
        );
    }

    public async Task RecordDeleteAsync
    (
        long              roundID,
        string            tableName,
        long              recordID,
        string            oldDataJSON,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            """
            INSERT INTO round_changes (round_id, table_name, record_id, operation, old_data, created_at)
            VALUES (@roundID, @tableName, @recordID, 'delete', @oldData, @createdAt)
            """,
            new
            {
                roundID,
                tableName,
                recordID,
                oldData   = oldDataJSON,
                createdAt = DateTime.UtcNow.ToString("O")
            }
        );
    }

    public async Task<IReadOnlyList<RoundChange>> GetByRoundAsync
    (
        long              roundID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<RoundChangeRow>
                   (
                       "SELECT * FROM round_changes WHERE round_id = @roundID ORDER BY id DESC",
                       new { roundID }
                   );

        return rows.Select(r => r.ToRoundChange()).ToList();
    }

    public async Task RollbackRoundAsync
    (
        long              roundID,
        CancellationToken cancellationToken = default
    )
    {
        var changes = await GetByRoundAsync(roundID, cancellationToken);

        if (changes.Count == 0)
            return;

        await using var connection  = await connectionFactory.CreateAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await CleanupUntrackedDependentsAsync(connection, transaction, changes);

            foreach (var change in changes)
            {
                if (!ValidTables.Contains(change.TableName))
                {
                    Log.Warning("回滚跳过未知表: {TableName}", change.TableName);
                    continue;
                }

                if (CompositeKeys.TryGetValue(change.TableName, out var keyColumns))
                    await ReverseCompositeKeyChangeAsync(connection, transaction, change, keyColumns, cancellationToken);
                else
                    await ReverseSimpleChangeAsync(connection, transaction, change, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task CleanupUntrackedDependentsAsync
    (
        DbConnection               connection,
        DbTransaction              transaction,
        IReadOnlyList<RoundChange> changes
    )
    {
        var relationIDs = changes
                          .Where(c => c.TableName == "character_relations" && c.Operation == "create")
                          .Select(c => c.RecordID)
                          .ToList();

        if (relationIDs.Count > 0)
        {
            await connection.ExecuteAsync
            (
                "DELETE FROM character_relation_logs WHERE relation_id IN @ids",
                new { ids = relationIDs },
                transaction
            );
        }

        var characterIDs = changes
                           .Where(c => c.TableName == "characters" && c.Operation == "create")
                           .Select(c => c.RecordID)
                           .ToList();

        if (characterIDs.Count > 0)
        {
            await connection.ExecuteAsync
            (
                "DELETE FROM character_category_resolutions WHERE character_id IN @ids",
                new { ids = characterIDs },
                transaction
            );
        }
    }

    public async Task RemoveByRoundAsync
    (
        long              roundID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            "DELETE FROM round_changes WHERE round_id = @roundID",
            new { roundID }
        );
    }

    public async Task<IReadOnlyList<CapturedChange>> CaptureRoundDataAsync
    (
        long              roundID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<RoundChangeRow>
                   (
                       "SELECT * FROM round_changes WHERE round_id = @roundID ORDER BY id ASC",
                       new { roundID }
                   );

        var changes = rows.Select(r => r.ToRoundChange()).ToList();
        var result  = new List<CapturedChange>();

        foreach (var change in changes)
        {
            string? newDataJSON = null;

            if (change.Operation is "create" or "update")
                newDataJSON = await ReadCurrentRowDataAsync(connection, change, cancellationToken);

            result.Add
            (
                new CapturedChange
                {
                    TableName   = change.TableName,
                    RecordID    = change.RecordID,
                    Operation   = change.Operation,
                    OldDataJSON = change.OldData,
                    NewDataJSON = newDataJSON
                }
            );
        }

        return result;
    }

    public async Task ReplayChangesAsync
    (
        long                          targetRoundID,
        IReadOnlyList<CapturedChange> changes,
        CancellationToken             cancellationToken = default
    )
    {
        await using var connection  = await connectionFactory.CreateAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var change in changes)
            {
                if (!ValidTables.Contains(change.TableName))
                    continue;

                if (CompositeKeys.TryGetValue(change.TableName, out var keyColumns))
                    await ReplayCompositeKeyChangeAsync(connection, transaction, targetRoundID, change, keyColumns, cancellationToken);
                else
                    await ReplaySimpleChangeAsync(connection, transaction, targetRoundID, change, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<string?> ReadCurrentRowDataAsync
    (
        DbConnection      connection,
        RoundChange       change,
        CancellationToken cancellationToken
    )
    {
        if (CompositeKeys.TryGetValue(change.TableName, out var keyColumns))
        {
            var oldData = ParseOldData(change.OldData);

            if (oldData is null)
                return null;

            var whereClause = string.Join(" AND ", keyColumns.Select(c => $"{c} = @{c}"));
            var parameters  = new DynamicParameters();

            foreach (var col in keyColumns)
                parameters.Add(col, oldData[col]);

            var row = await connection.QueryFirstOrDefaultAsync<Dictionary<string, object>>
                      (
                          $"SELECT * FROM {change.TableName} WHERE {whereClause}",
                          parameters
                      );

            if (row is null)
                return null;

            var dict = new Dictionary<string, object?>();
            foreach (var kvp in row)
                dict[kvp.Key] = kvp.Value;

            return JsonSerializer.Serialize(dict);
        }
        else
        {
            var row = await connection.QueryFirstOrDefaultAsync<Dictionary<string, object>>
                      (
                          $"SELECT * FROM {change.TableName} WHERE id = @id",
                          new { id = change.RecordID }
                      );

            if (row is null)
                return null;

            var dict = new Dictionary<string, object?>();
            foreach (var kvp in row)
                dict[kvp.Key] = kvp.Value;

            return JsonSerializer.Serialize(dict);
        }
    }

    private static async Task ReplaySimpleChangeAsync
    (
        DbConnection      connection,
        DbTransaction     transaction,
        long              targetRoundID,
        CapturedChange    change,
        CancellationToken cancellationToken
    )
    {
        switch (change.Operation)
        {
            case "create":
                await InsertRowAsync(connection, transaction, change.TableName, change.NewDataJSON);
                await connection.ExecuteAsync
                (
                    """
                    INSERT INTO round_changes (round_id, table_name, record_id, operation, old_data, created_at)
                    VALUES (@roundID, @tableName, @recordID, 'create', NULL, @createdAt)
                    """,
                    new
                    {
                        roundID   = targetRoundID,
                        tableName = change.TableName,
                        recordID  = change.RecordID,
                        createdAt = DateTime.UtcNow.ToString("O")
                    },
                    transaction
                );
                break;

            case "update":
                await UpsertRowAsync(connection, transaction, change.TableName, change.RecordID, change.NewDataJSON);
                await connection.ExecuteAsync
                (
                    """
                    INSERT INTO round_changes (round_id, table_name, record_id, operation, old_data, created_at)
                    VALUES (@roundID, @tableName, @recordID, 'update', @oldData, @createdAt)
                    """,
                    new
                    {
                        roundID   = targetRoundID,
                        tableName = change.TableName,
                        recordID  = change.RecordID,
                        oldData   = change.OldDataJSON,
                        createdAt = DateTime.UtcNow.ToString("O")
                    },
                    transaction
                );
                break;

            case "delete":
                await CleanupUntrackedDependentsForRecordAsync(connection, transaction, change.TableName, change.RecordID);
                await connection.ExecuteAsync
                (
                    $"DELETE FROM {change.TableName} WHERE id = @id",
                    new { id = change.RecordID },
                    transaction
                );
                await connection.ExecuteAsync
                (
                    """
                    INSERT INTO round_changes (round_id, table_name, record_id, operation, old_data, created_at)
                    VALUES (@roundID, @tableName, @recordID, 'delete', @oldData, @createdAt)
                    """,
                    new
                    {
                        roundID   = targetRoundID,
                        tableName = change.TableName,
                        recordID  = change.RecordID,
                        oldData   = change.OldDataJSON,
                        createdAt = DateTime.UtcNow.ToString("O")
                    },
                    transaction
                );
                break;
        }
    }

    private static async Task ReplayCompositeKeyChangeAsync
    (
        DbConnection      connection,
        DbTransaction     transaction,
        long              targetRoundID,
        CapturedChange    change,
        string[]          keyColumns,
        CancellationToken cancellationToken
    )
    {
        switch (change.Operation)
        {
            case "create":
                await InsertRowAsync(connection, transaction, change.TableName, change.NewDataJSON);
                await connection.ExecuteAsync
                (
                    """
                    INSERT INTO round_changes (round_id, table_name, record_id, operation, old_data, created_at)
                    VALUES (@roundID, @tableName, @recordID, 'create', @oldData, @createdAt)
                    """,
                    new
                    {
                        roundID   = targetRoundID,
                        tableName = change.TableName,
                        recordID  = change.RecordID,
                        oldData   = change.OldDataJSON,
                        createdAt = DateTime.UtcNow.ToString("O")
                    },
                    transaction
                );
                break;

            case "update":
                await UpsertCompositeRowAsync(connection, transaction, change.TableName, change.NewDataJSON, keyColumns);
                await connection.ExecuteAsync
                (
                    """
                    INSERT INTO round_changes (round_id, table_name, record_id, operation, old_data, created_at)
                    VALUES (@roundID, @tableName, @recordID, 'update', @oldData, @createdAt)
                    """,
                    new
                    {
                        roundID   = targetRoundID,
                        tableName = change.TableName,
                        recordID  = change.RecordID,
                        oldData   = change.OldDataJSON,
                        createdAt = DateTime.UtcNow.ToString("O")
                    },
                    transaction
                );
                break;

            case "delete":
            {
                var oldData = ParseOldData(change.OldDataJSON);

                if (oldData is not null)
                {
                    var whereClause = string.Join(" AND ", keyColumns.Select((c, i) => $"{c} = @key{i}"));
                    var parameters  = new DynamicParameters();

                    for (var i = 0; i < keyColumns.Length; i++)
                        parameters.Add($"key{i}", oldData[keyColumns[i]]);

                    await connection.ExecuteAsync
                    (
                        $"DELETE FROM {change.TableName} WHERE {whereClause}",
                        parameters,
                        transaction
                    );
                }

                await connection.ExecuteAsync
                (
                    """
                    INSERT INTO round_changes (round_id, table_name, record_id, operation, old_data, created_at)
                    VALUES (@roundID, @tableName, @recordID, 'delete', @oldData, @createdAt)
                    """,
                    new
                    {
                        roundID   = targetRoundID,
                        tableName = change.TableName,
                        recordID  = change.RecordID,
                        oldData   = change.OldDataJSON,
                        createdAt = DateTime.UtcNow.ToString("O")
                    },
                    transaction
                );
                break;
            }
        }
    }

    private static async Task InsertRowAsync
    (
        DbConnection  connection,
        DbTransaction transaction,
        string        tableName,
        string?       dataJSON
    )
    {
        var data = ParseOldData(dataJSON);

        if (data is null || data.Count == 0)
            return;

        var columns      = data.Keys.ToList();
        var placeholders = columns.Select(c => $"@{c}").ToList();
        var parameters   = new DynamicParameters();

        foreach (var (key, value) in data)
            parameters.Add(key, value);

        await connection.ExecuteAsync
        (
            $"INSERT OR REPLACE INTO {tableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})",
            parameters,
            transaction
        );
    }

    private static async Task UpsertRowAsync
    (
        DbConnection  connection,
        DbTransaction transaction,
        string        tableName,
        long          recordID,
        string?       dataJSON
    )
    {
        var data = ParseOldData(dataJSON);

        if (data is null || data.Count == 0)
            return;

        var exists = await connection.ExecuteScalarAsync<long>
                     (
                         $"SELECT COUNT(*) FROM {tableName} WHERE id = @id",
                         new { id = recordID },
                         transaction
                     );

        if (exists > 0)
        {
            var setClauses = data
                             .Where(kvp => kvp.Key != "id")
                             .Select(kvp => $"{kvp.Key} = @{kvp.Key}")
                             .ToList();

            if (setClauses.Count == 0)
                return;

            var parameters = new DynamicParameters();

            foreach (var (key, value) in data)
            {
                if (key != "id")
                    parameters.Add(key, value);
            }

            parameters.Add("id", recordID);

            await connection.ExecuteAsync
            (
                $"UPDATE {tableName} SET {string.Join(", ", setClauses)} WHERE id = @id",
                parameters,
                transaction
            );
        }
        else
            await InsertRowAsync(connection, transaction, tableName, dataJSON);
    }

    private static async Task UpsertCompositeRowAsync
    (
        DbConnection  connection,
        DbTransaction transaction,
        string        tableName,
        string?       dataJSON,
        string[]      keyColumns
    )
    {
        var data = ParseOldData(dataJSON);

        if (data is null || data.Count == 0)
            return;

        var whereClause = string.Join(" AND ", keyColumns.Select((c, i) => $"{c} = @key{i}"));
        var checkParams = new DynamicParameters();

        for (var i = 0; i < keyColumns.Length; i++)
            checkParams.Add($"key{i}", data[keyColumns[i]]);

        var exists = await connection.ExecuteScalarAsync<long>
                     (
                         $"SELECT COUNT(*) FROM {tableName} WHERE {whereClause}",
                         checkParams,
                         transaction
                     );

        if (exists > 0)
        {
            var setClauses = data
                             .Where(kvp => !keyColumns.Contains(kvp.Key))
                             .Select(kvp => $"{kvp.Key} = @{kvp.Key}")
                             .ToList();

            if (setClauses.Count == 0)
                return;

            var parameters = new DynamicParameters();

            foreach (var (key, value) in data)
            {
                if (!keyColumns.Contains(key))
                    parameters.Add(key, value);
            }

            for (var i = 0; i < keyColumns.Length; i++)
                parameters.Add($"wkey{i}", data[keyColumns[i]]);

            var whereParts = keyColumns.Select((c, i) => $"{c} = @wkey{i}");
            await connection.ExecuteAsync
            (
                $"UPDATE {tableName} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereParts)}",
                parameters,
                transaction
            );
        }
        else
            await InsertRowAsync(connection, transaction, tableName, dataJSON);
    }

    private static async Task CleanupUntrackedDependentsForRecordAsync
    (
        DbConnection  connection,
        DbTransaction transaction,
        string        tableName,
        long          recordID
    )
    {
        if (tableName == "character_relations")
        {
            await connection.ExecuteAsync
            (
                "DELETE FROM character_relation_logs WHERE relation_id = @id",
                new { id = recordID },
                transaction
            );
        }
        else if (tableName == "characters")
        {
            await connection.ExecuteAsync
            (
                "DELETE FROM character_category_resolutions WHERE character_id = @id",
                new { id = recordID },
                transaction
            );
        }
    }

    private static async Task ReverseSimpleChangeAsync
    (
        DbConnection      connection,
        DbTransaction     transaction,
        RoundChange       change,
        CancellationToken cancellationToken
    )
    {
        switch (change.Operation)
        {
            case "create":
                await connection.ExecuteAsync
                (
                    $"DELETE FROM {change.TableName} WHERE id = @id",
                    new { id = change.RecordID },
                    transaction
                );
                break;

            case "update":
                await ReverseUpdateAsync(connection, transaction, change, "id", [change.RecordID], cancellationToken);
                break;

            case "delete":
                await ReverseDeleteAsync(connection, transaction, change, cancellationToken);
                break;
        }
    }

    private static async Task ReverseCompositeKeyChangeAsync
    (
        DbConnection      connection,
        DbTransaction     transaction,
        RoundChange       change,
        string[]          keyColumns,
        CancellationToken cancellationToken
    )
    {
        var oldData = ParseOldData(change.OldData);

        if (oldData is null)
            return;

        var keyValues = keyColumns.Select(c => oldData[c]).ToArray();

        switch (change.Operation)
        {
            case "create":
            {
                var whereClause = string.Join(" AND ", keyColumns.Select((c, i) => $"{c} = @key{i}"));
                var parameters  = new DynamicParameters();
                for (var i = 0; i < keyColumns.Length; i++)
                    parameters.Add($"key{i}", keyValues[i]);

                await connection.ExecuteAsync
                (
                    $"DELETE FROM {change.TableName} WHERE {whereClause}",
                    parameters,
                    transaction
                );
                break;
            }

            case "update":
                await ReverseUpdateAsync(connection, transaction, change, null, null, oldData, keyColumns, cancellationToken);
                break;

            case "delete":
            {
                var columns      = oldData.Keys.ToList();
                var placeholders = columns.Select(c => $"@{c}").ToList();
                var parameters   = new DynamicParameters();
                foreach (var (key, value) in oldData)
                    parameters.Add(key, value);

                await connection.ExecuteAsync
                (
                    $"INSERT INTO {change.TableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})",
                    parameters,
                    transaction
                );
                break;
            }
        }
    }

    private static async Task ReverseUpdateAsync
    (
        DbConnection      connection,
        DbTransaction     transaction,
        RoundChange       change,
        string?           keyColumn,
        object[]?         keyValues,
        CancellationToken cancellationToken
    )
    {
        var oldData = ParseOldData(change.OldData);

        if (oldData is null)
            return;

        await ReverseUpdateAsync(connection, transaction, change, keyColumn, keyValues, oldData, null, cancellationToken);
    }

    private static async Task ReverseUpdateAsync
    (
        DbConnection                connection,
        DbTransaction               transaction,
        RoundChange                 change,
        string?                     keyColumn,
        object[]?                   keyValues,
        Dictionary<string, object?> oldData,
        string[]?                   compositeKeyColumns,
        CancellationToken           cancellationToken
    )
    {
        var setClauses = new List<string>();
        var parameters = new DynamicParameters();

        foreach (var (key, value) in oldData)
        {
            if (key == "id" || (compositeKeyColumns?.Contains(key) ?? false))
                continue;

            setClauses.Add($"{key} = @{key}");
            parameters.Add(key, value);
        }

        if (setClauses.Count == 0)
            return;

        string whereClause;

        if (keyColumn is not null && keyValues is not null)
        {
            whereClause = $"{keyColumn} = @id";
            parameters.Add("id", keyValues[0]);
        }
        else if (compositeKeyColumns is not null)
        {
            var whereParts = new List<string>();

            for (var i = 0; i < compositeKeyColumns.Length; i++)
            {
                whereParts.Add($"{compositeKeyColumns[i]} = @wkey{i}");
                parameters.Add($"wkey{i}", oldData[compositeKeyColumns[i]]);
            }

            whereClause = string.Join(" AND ", whereParts);
        }
        else
        {
            whereClause = "id = @id";
            parameters.Add("id", change.RecordID);
        }

        await connection.ExecuteAsync
        (
            $"UPDATE {change.TableName} SET {string.Join(", ", setClauses)} WHERE {whereClause}",
            parameters,
            transaction
        );
    }

    private static async Task ReverseDeleteAsync
    (
        DbConnection      connection,
        DbTransaction     transaction,
        RoundChange       change,
        CancellationToken cancellationToken
    )
    {
        var oldData = ParseOldData(change.OldData);

        if (oldData is null)
            return;

        var columns      = oldData.Keys.ToList();
        var placeholders = columns.Select(c => $"@{c}").ToList();
        var parameters   = new DynamicParameters();

        foreach (var (key, value) in oldData)
            parameters.Add(key, value);

        await connection.ExecuteAsync
        (
            $"INSERT INTO {change.TableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})",
            parameters,
            transaction
        );
    }

    private static Dictionary<string, object?>? ParseOldData(string? jsonData)
    {
        if (string.IsNullOrWhiteSpace(jsonData))
            return null;

        var doc    = JsonDocument.Parse(jsonData);
        var result = new Dictionary<string, object?>();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ?
                                            l :
                                            prop.Value.GetDouble(),
                JsonValueKind.True  => 1L,
                JsonValueKind.False => 0L,
                JsonValueKind.Null  => null,
                _                   => prop.Value.GetRawText()
            };
        }

        return result;
    }

    public static async Task<string> ReadRowAsJSONAsync
    (
        DbConnection      connection,
        string            tableName,
        long              recordID,
        CancellationToken cancellationToken = default
    )
    {
        var row = await connection.QueryFirstOrDefaultAsync<Dictionary<string, object>>
                  (
                      $"SELECT * FROM {tableName} WHERE id = @id",
                      new { id = recordID }
                  );

        if (row is null)
            return "{}";

        var dict = new Dictionary<string, object?>();
        foreach (var kvp in row)
            dict[kvp.Key] = kvp.Value;

        return JsonSerializer.Serialize(dict);
    }

    private sealed class RoundChangeRow
    {
        public long    ID         { get; set; }
        public long    Round_ID   { get; set; }
        public string  Table_Name { get; set; } = string.Empty;
        public long    Record_ID  { get; set; }
        public string  Operation  { get; set; } = string.Empty;
        public string? Old_Data   { get; set; }
        public string  Created_At { get; set; } = string.Empty;

        public RoundChange ToRoundChange() =>
            new()
            {
                ID        = ID,
                RoundID   = Round_ID,
                TableName = Table_Name,
                RecordID  = Record_ID,
                Operation = Operation,
                OldData   = Old_Data,
                CreatedAt = DateTime.Parse(Created_At)
            };
    }
}
