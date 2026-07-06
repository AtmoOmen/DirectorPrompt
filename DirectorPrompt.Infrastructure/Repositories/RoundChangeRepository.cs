using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Data.Sqlite;
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
        ["character_state_values"]    = ["character_id", "attribute_id"],
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
        string?           oldDataJSON = null,
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
                oldData = oldDataJSON,
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
                oldData = oldDataJSON,
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
                oldData = oldDataJSON,
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

    private static async Task ReverseSimpleChangeAsync
    (
        DbConnection       connection,
        DbTransaction      transaction,
        RoundChange         change,
        CancellationToken   cancellationToken
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
        DbConnection       connection,
        DbTransaction      transaction,
        RoundChange         change,
        string[]            keyColumns,
        CancellationToken   cancellationToken
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
                var parameters = new DynamicParameters();
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
                var columns = oldData.Keys.ToList();
                var placeholders = columns.Select(c => $"@{c}").ToList();
                var parameters = new DynamicParameters();
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
        DbConnection       connection,
        DbTransaction      transaction,
        RoundChange         change,
        string?             keyColumn,
        object[]?           keyValues,
        CancellationToken   cancellationToken
    )
    {
        var oldData = ParseOldData(change.OldData);

        if (oldData is null)
            return;

        await ReverseUpdateAsync(connection, transaction, change, keyColumn, keyValues, oldData, null, cancellationToken);
    }

    private static async Task ReverseUpdateAsync
    (
        DbConnection                  connection,
        DbTransaction                 transaction,
        RoundChange                   change,
        string?                       keyColumn,
        object[]?                     keyValues,
        Dictionary<string, object?>   oldData,
        string[]?                     compositeKeyColumns,
        CancellationToken             cancellationToken
    )
    {
        var setClauses = new List<string>();
        var parameters = new DynamicParameters();

        foreach (var (key, value) in oldData)
        {
            if (key == "id" || (compositeKeyColumns?.Contains(key) ?? false))
                continue;

            setClauses.Add($"{key} = @{key}");
            parameters.Add(key, value ?? DBNull.Value);
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
        DbConnection       connection,
        DbTransaction      transaction,
        RoundChange         change,
        CancellationToken   cancellationToken
    )
    {
        var oldData = ParseOldData(change.OldData);

        if (oldData is null)
            return;

        var columns = oldData.Keys.ToList();
        var placeholders = columns.Select(c => $"@{c}").ToList();
        var parameters = new DynamicParameters();

        foreach (var (key, value) in oldData)
            parameters.Add(key, value ?? DBNull.Value);

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

        var doc = JsonDocument.Parse(jsonData);
        var result = new Dictionary<string, object?>();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String  => prop.Value.GetString(),
                JsonValueKind.Number  => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                JsonValueKind.True    => 1L,
                JsonValueKind.False   => 0L,
                JsonValueKind.Null    => null,
                _                     => prop.Value.GetRawText()
            };
        }

        return result;
    }

    public static async Task<string> ReadRowAsJSONAsync
    (
        DbConnection       connection,
        string             tableName,
        long               recordID,
        CancellationToken  cancellationToken = default
    )
    {
        var row = await connection.QueryFirstOrDefaultAsync<IDictionary<string, object>>
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
        public long   ID         { get; set; }
        public long   Round_ID   { get; set; }
        public string Table_Name { get; set; } = string.Empty;
        public long   Record_ID  { get; set; }
        public string Operation  { get; set; } = string.Empty;
        public string? Old_Data  { get; set; }
        public string Created_At { get; set; } = string.Empty;

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
