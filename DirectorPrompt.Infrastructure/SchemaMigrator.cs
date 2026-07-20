using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Serilog;

namespace DirectorPrompt.Infrastructure;

public sealed class SchemaMigrator
{
    private readonly SQLiteDatabaseScheduler databaseScheduler;
    private readonly Assembly                assembly = Assembly.GetExecutingAssembly();

    public SchemaMigrator(SQLiteDatabaseScheduler databaseScheduler) =>
        this.databaseScheduler = databaseScheduler;

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        Log.Information("开始执行数据库架构迁移");

        try
        {
            await databaseScheduler.ExecuteAsync(MigrateAsync, cancellationToken: cancellationToken);
            Log.Information("数据库架构迁移完成: 耗时={ElapsedMilliseconds}ms", stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log.Information("数据库架构迁移已取消: 耗时={ElapsedMilliseconds}ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "数据库架构迁移失败: 耗时={ElapsedMilliseconds}ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var fkCommand = connection.CreateCommand())
        {
            fkCommand.CommandText = "PRAGMA foreign_keys = OFF;";
            await fkCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var currentVersion = await GetCurrentVersionAsync(connection, cancellationToken);
        var scripts        = GetMigrationScripts();
        var appliedCount   = 0;

        Log.Information
        (
            "数据库架构状态: 当前版本={CurrentVersion}, 可用迁移数={ScriptCount}",
            currentVersion,
            scripts.Count
        );

        foreach (var (version, scriptName) in scripts)
        {
            if (version <= currentVersion)
                continue;

            var sql = await ReadEmbeddedScriptAsync(scriptName, cancellationToken);
            var stopwatch = Stopwatch.StartNew();

            Log.Information("开始应用数据库迁移: 版本={Version}, 脚本={ScriptName}", version, scriptName);

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                await ExecuteSQLScriptAsync(connection, transaction, sql, cancellationToken);

                await using var logCommand = connection.CreateCommand();
                logCommand.Transaction = transaction;
                logCommand.CommandText = "INSERT INTO schema_version (version, applied_at) VALUES ($version, $appliedAt)";
                logCommand.Parameters.AddWithValue("$version",   version);
                logCommand.Parameters.AddWithValue("$appliedAt", DateTime.UtcNow.ToString("O"));
                await logCommand.ExecuteNonQueryAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                appliedCount++;

                Log.Information
                (
                    "数据库迁移已应用: 版本={Version}, 脚本={ScriptName}, SQL长度={SqlLength}, 耗时={ElapsedMilliseconds}ms",
                    version,
                    scriptName,
                    sql.Length,
                    stopwatch.ElapsedMilliseconds
                );
            }
            catch (Exception exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                Log.Error
                (
                    exception,
                    "数据库迁移失败并已回滚: 版本={Version}, 脚本={ScriptName}, 耗时={ElapsedMilliseconds}ms",
                    version,
                    scriptName,
                    stopwatch.ElapsedMilliseconds
                );
                throw;
            }
        }

        Log.Information
        (
            "数据库架构检查完成: 初始版本={CurrentVersion}, 已应用迁移数={AppliedCount}",
            currentVersion,
            appliedCount
        );
    }

    private static async Task<int> GetCurrentVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT name FROM sqlite_master
                              WHERE type='table' AND name='schema_version'
                              """;

        var result = await command.ExecuteScalarAsync(cancellationToken);

        if (result is null)
            return 0;

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT MAX(version) FROM schema_version";
        var versionResult = await versionCommand.ExecuteScalarAsync(cancellationToken);

        return versionResult is long maxVersion ?
                   (int)maxVersion :
                   0;
    }

    private List<(int version, string scriptName)> GetMigrationScripts() =>
        assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith("DirectorPrompt.Infrastructure.Schema.", StringComparison.Ordinal))
                .Select
                (name =>
                    {
                        var withoutPrefix = name["DirectorPrompt.Infrastructure.Schema.".Length..];
                        var withoutSuffix = withoutPrefix.EndsWith(".sql", StringComparison.Ordinal) ?
                                                withoutPrefix[..^4] :
                                                withoutPrefix;
                        var versionStr = withoutSuffix.Split('_')[0];
                        return (version: int.Parse(versionStr), scriptName: name);
                    }
                )
                .OrderBy(x => x.version)
                .ToList();

    private async Task<string> ReadEmbeddedScriptAsync(string scriptName, CancellationToken cancellationToken)
    {
        await using var stream = assembly.GetManifestResourceStream(scriptName);

        if (stream is null)
            throw new FileNotFoundException($"嵌入资源 {scriptName} 不存在");

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task ExecuteSQLScriptAsync
    (
        SqliteConnection  connection,
        SqliteTransaction transaction,
        string            sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
