using DirectorPrompt.Infrastructure;
using Xunit;

namespace DirectorPrompt.Tests;

public sealed class SQLiteDatabaseSchedulerTests
{
    [Fact]
    public async Task ExecuteAsyncReusesConnectionAndPersistsWork()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"directorprompt-{Guid.NewGuid():N}.db");

        try
        {
            var             factory   = new SQLiteConnectionFactory($"Data Source={databasePath};Pooling=False", false);
            await using var scheduler = new SQLiteDatabaseScheduler(factory);

            await scheduler.ExecuteAsync
            (async (connection, token) =>
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText =
                        """
                        CREATE TABLE values_table(value INTEGER NOT NULL); INSERT INTO values_table VALUES (42);
                        """;
                    await command.ExecuteNonQueryAsync(token);
                }
            );

            var value = await scheduler.ExecuteAsync
                        (async (connection, token) =>
                            {
                                await using var command = connection.CreateCommand();
                                command.CommandText = "SELECT value FROM values_table";
                                return Convert.ToInt32(await command.ExecuteScalarAsync(token));
                            }
                        );

            Assert.Equal(42, value);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task ForegroundWorkRunsBeforeQueuedMaintenanceWork()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"directorprompt-{Guid.NewGuid():N}.db");

        try
        {
            var             factory   = new SQLiteConnectionFactory($"Data Source={databasePath};Pooling=False", false);
            await using var scheduler = new SQLiteDatabaseScheduler(factory);
            var             started   = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var             release   = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var             order     = new List<string>();
            var blocker = scheduler.ExecuteAsync
            (async (_, _) =>
                {
                    started.SetResult();
                    await release.Task;
                }
            );
            await started.Task;
            var maintenance = scheduler.ExecuteAsync
            (
                (_, _) =>
                {
                    order.Add("maintenance");
                    return Task.CompletedTask;
                },
                SQLiteWorkPriority.Maintenance
            );
            var foreground = scheduler.ExecuteAsync
            ((_, _) =>
                {
                    order.Add("foreground");
                    return Task.CompletedTask;
                }
            );
            release.SetResult();
            await Task.WhenAll(blocker, maintenance, foreground);

            Assert.Equal(["foreground", "maintenance"], order);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task CancelledQueuedWorkDoesNotExecute()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"directorprompt-{Guid.NewGuid():N}.db");

        try
        {
            var             factory   = new SQLiteConnectionFactory($"Data Source={databasePath};Pooling=False", false);
            await using var scheduler = new SQLiteDatabaseScheduler(factory);
            var             started   = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var             release   = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocker = scheduler.ExecuteAsync
            (async (_, _) =>
                {
                    started.SetResult();
                    await release.Task;
                }
            );
            await started.Task;
            using var cancellationSource = new CancellationTokenSource();
            var       executed           = false;
            var cancelled = scheduler.ExecuteAsync
            (
                (_, _) =>
                {
                    executed = true;
                    return Task.CompletedTask;
                },
                SQLiteWorkPriority.Maintenance,
                cancellationSource.Token
            );
            cancellationSource.Cancel();
            release.SetResult();
            await blocker;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            Assert.False(executed);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
