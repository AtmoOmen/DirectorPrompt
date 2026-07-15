using System.Data.Common;
using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Serilog;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class RoundChangeRepository
(
    SQLiteDatabaseScheduler scheduler
) : IRoundChangeRepository
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

    public Task RecordCreateAsync
    (
        long              sessionID,
        long              roundID,
        string            tableName,
        long              recordID,
        string?           oldDataJSON       = null,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            (connection, token) => RecordAsync
            (
                connection,
                null,
                sessionID,
                roundID,
                tableName,
                recordID,
                "create",
                oldDataJSON,
                token
            ),
            cancellationToken: cancellationToken
        );

    public Task RecordUpdateAsync
    (
        long              sessionID,
        long              roundID,
        string            tableName,
        long              recordID,
        string            oldDataJSON,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            (connection, token) => RecordAsync
            (
                connection,
                null,
                sessionID,
                roundID,
                tableName,
                recordID,
                "update",
                oldDataJSON,
                token
            ),
            cancellationToken: cancellationToken
        );

    public Task RecordDeleteAsync
    (
        long              sessionID,
        long              roundID,
        string            tableName,
        long              recordID,
        string            oldDataJSON,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            (connection, token) => RecordAsync
            (
                connection,
                null,
                sessionID,
                roundID,
                tableName,
                recordID,
                "delete",
                oldDataJSON,
                token
            ),
            cancellationToken: cancellationToken
        );

    public Task<IReadOnlyList<RoundChange>> GetByRoundAsync
    (
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync<IReadOnlyList<RoundChange>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<RoundChange>
                           (
                               new CommandDefinition
                               (
                                   "SELECT * FROM round_changes WHERE session_id = @sessionID AND round_id = @roundID ORDER BY id DESC",
                                   new { sessionID, roundID },
                                   cancellationToken: token
                               )
                           );

                return rows.ToList();
            },
            cancellationToken: cancellationToken
        );

    public Task RollbackRoundAsync
    (
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                var changes = (await connection.QueryAsync<RoundChange>
                               (
                                   new CommandDefinition
                                   (
                                       "SELECT * FROM round_changes WHERE session_id = @sessionID AND round_id = @roundID ORDER BY id DESC",
                                       new { sessionID, roundID },
                                       cancellationToken: token
                                   )
                               )).ToList();

                if (changes.Count == 0)
                    return;

                await using var transaction = await connection.BeginTransactionAsync(token);

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
                            await ReverseCompositeKeyChangeAsync(connection, transaction, change, keyColumns, token);
                        else
                            await ReverseSimpleChangeAsync(connection, transaction, change, token);
                    }

                    await transaction.CommitAsync(token);
                }
                catch
                {
                    await transaction.RollbackAsync(token);
                    throw;
                }
            },
            cancellationToken: cancellationToken
        );

    private static async Task CleanupUntrackedDependentsAsync
    (
        DbConnection               connection,
        DbTransaction              transaction,
        IReadOnlyList<RoundChange> changes
    )
    {
        var relationIDs = changes
                          .Where(c => c is { TableName: "character_relations", Operation: "create" })
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
                           .Where(c => c is { TableName: "characters", Operation: "create" })
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

    public Task RemoveByRoundAsync
    (
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        "DELETE FROM round_changes WHERE session_id = @sessionID AND round_id = @roundID",
                        new { sessionID, roundID },
                        cancellationToken: token
                    )
                );
            },
            cancellationToken: cancellationToken
        );

    internal static async Task RecordAsync
    (
        DbConnection      connection,
        DbTransaction?    transaction,
        long              sessionID,
        long              roundID,
        string            tableName,
        long              recordID,
        string            operation,
        string?           oldDataJSON,
        CancellationToken cancellationToken
    ) =>
        await connection.ExecuteAsync
        (
            new CommandDefinition
            (
                """
                INSERT INTO round_changes (session_id, round_id, table_name, record_id, operation, old_data, created_at)
                VALUES (@sessionID, @roundID, @tableName, @recordID, @operation, @oldData, @createdAt)
                """,
                new
                {
                    sessionID,
                    roundID,
                    tableName,
                    recordID,
                    operation,
                    oldData   = oldDataJSON,
                    createdAt = DateTime.UtcNow
                },
                transaction,
                cancellationToken: cancellationToken
            )
        );

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
}
