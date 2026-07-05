using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace DirectorPrompt.Infrastructure;

public sealed class SqliteConnectionFactory
{
    private readonly string connectionString;
    private readonly bool   enableVecExtension;

    public SqliteConnectionFactory(string connectionString, bool enableVecExtension = true)
    {
        this.connectionString   = connectionString;
        this.enableVecExtension = enableVecExtension;
    }

    public async Task<SqliteConnection> CreateAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        if (enableVecExtension)
            await LoadVecExtensionAsync(connection, cancellationToken);

        return connection;
    }

    private static async Task LoadVecExtensionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var vecPath = FindVecLibrary();

        if (vecPath is not null)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT load_extension($path)";
            command.Parameters.AddWithValue("$path", vecPath);
            await command.ExecuteScalarAsync(cancellationToken);
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
