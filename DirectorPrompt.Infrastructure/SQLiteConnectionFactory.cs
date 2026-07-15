using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Serilog;

namespace DirectorPrompt.Infrastructure;

public sealed class SQLiteConnectionFactory
(
    string connectionString,
    bool   enableVecExtension = true
)
{
    private readonly string? vecPath = enableVecExtension ?
                                           FindVecLibrary() :
                                           null;

    public async Task<SqliteConnection> CreateAsync
    (
        bool              loadVectorExtension = false,
        CancellationToken cancellationToken   = default
    )
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        if (enableVecExtension && loadVectorExtension)
            TryLoadVecExtension(connection, vecPath);

        return connection;
    }

    private static void TryLoadVecExtension(SqliteConnection connection, string? vecPath)
    {
        if (vecPath is null)
        {
            Log.Warning("sqlite-vec 原生库未找到, 向量检索功能不可用");
            return;
        }

        try
        {
            connection.LoadExtension(vecPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "sqlite-vec 扩展加载失败, 向量检索功能不可用");
        }
    }

    private static string? FindVecLibrary()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;

        var fileName = rid.Contains("win") ? "vec0.dll" : rid.Contains("osx") ? "vec0.dylib" : "vec0.so";

        var baseDir = AppContext.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(baseDir, fileName),
            Path.Combine(baseDir, "runtimes", rid, "native", fileName),
            Path.Combine(baseDir, "native",   fileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
