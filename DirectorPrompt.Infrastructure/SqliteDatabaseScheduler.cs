using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace DirectorPrompt.Infrastructure;

public sealed class SqliteDatabaseScheduler : IAsyncDisposable
{
    private const int FOREGROUND_CAPACITY  = 256;
    private const int MAINTENANCE_CAPACITY = 64;

    private readonly SqliteConnectionFactory connectionFactory;
    private readonly Channel<WorkItem>       foregroundQueue;
    private readonly Channel<WorkItem>       maintenanceQueue;
    private readonly CancellationTokenSource shutdownSource = new();
    private readonly Task                    worker;

    public SqliteDatabaseScheduler(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
        foregroundQueue        = CreateQueue(FOREGROUND_CAPACITY);
        maintenanceQueue       = CreateQueue(MAINTENANCE_CAPACITY);
        worker                 = Task.Run(RunAsync);
    }

    public Task ExecuteAsync
    (
        Func<SqliteConnection, CancellationToken, Task> operation,
        SqliteWorkPriority                              priority          = SqliteWorkPriority.Foreground,
        CancellationToken                               cancellationToken = default
    ) =>
        ExecuteAsync
        (
            async (connection, token) =>
            {
                await operation(connection, token);
                return true;
            },
            priority,
            cancellationToken
        );

    public async Task<TResult> ExecuteAsync<TResult>
    (
        Func<SqliteConnection, CancellationToken, Task<TResult>> operation,
        SqliteWorkPriority                                       priority          = SqliteWorkPriority.Foreground,
        CancellationToken                                        cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var item = new WorkItem<TResult>(operation, cancellationToken);
        var writer = priority == SqliteWorkPriority.Foreground ?
                         foregroundQueue.Writer :
                         maintenanceQueue.Writer;

        await writer.WriteAsync(item, cancellationToken);
        return await item.Task;
    }

    public async ValueTask DisposeAsync()
    {
        foregroundQueue.Writer.TryComplete();
        maintenanceQueue.Writer.TryComplete();

        await worker;
        shutdownSource.Cancel();
        shutdownSource.Dispose();
    }

    private static Channel<WorkItem> CreateQueue(int capacity) =>
        Channel.CreateBounded<WorkItem>
        (
            new BoundedChannelOptions(capacity)
            {
                FullMode                      = BoundedChannelFullMode.Wait,
                SingleReader                  = true,
                SingleWriter                  = false,
                AllowSynchronousContinuations = false
            }
        );

    private async Task RunAsync()
    {
        try
        {
            await using var connection = await connectionFactory.CreateAsync(shutdownSource.Token, true);
            await ConfigureConnectionAsync(connection, shutdownSource.Token);

            while (await WaitForWorkAsync())
            {
                if (!foregroundQueue.Reader.TryRead(out var item))
                    maintenanceQueue.Reader.TryRead(out item);

                if (item is not null)
                    await item.ExecuteAsync(connection, shutdownSource.Token);
            }
        }
        catch (Exception ex)
        {
            foregroundQueue.Writer.TryComplete(ex);
            maintenanceQueue.Writer.TryComplete(ex);

            while (foregroundQueue.Reader.TryRead(out var foregroundItem))
                foregroundItem.Fail(ex);

            while (maintenanceQueue.Reader.TryRead(out var maintenanceItem))
                maintenanceItem.Fail(ex);
        }
    }

    private async Task<bool> WaitForWorkAsync()
    {
        while (true)
        {
            if (foregroundQueue.Reader.TryPeek(out _) || maintenanceQueue.Reader.TryPeek(out _))
                return true;

            var foregroundWait  = foregroundQueue.Reader.WaitToReadAsync(shutdownSource.Token).AsTask();
            var maintenanceWait = maintenanceQueue.Reader.WaitToReadAsync(shutdownSource.Token).AsTask();
            await Task.WhenAny(foregroundWait, maintenanceWait);

            if (foregroundQueue.Reader.TryPeek(out _) || maintenanceQueue.Reader.TryPeek(out _))
                return true;

            if (foregroundQueue.Reader.Completion.IsCompleted && maintenanceQueue.Reader.Completion.IsCompleted)
                return false;
        }
    }

    private static async Task ConfigureConnectionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              PRAGMA journal_mode=WAL;
                              PRAGMA synchronous=NORMAL;
                              PRAGMA foreign_keys=ON;
                              PRAGMA busy_timeout=5000;
                              PRAGMA cache_size=-8192;
                              PRAGMA journal_size_limit=16777216;
                              PRAGMA temp_store=FILE;
                              """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private abstract class WorkItem
    {
        public abstract Task ExecuteAsync(SqliteConnection connection, CancellationToken shutdownToken);

        public abstract void Fail(Exception exception);
    }

    private sealed class WorkItem<TResult>
    (
        Func<SqliteConnection, CancellationToken, Task<TResult>> operation,
        CancellationToken                                        cancellationToken
    ) : WorkItem
    {
        private readonly TaskCompletionSource<TResult> completionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TResult> Task => completionSource.Task;

        public override async Task ExecuteAsync(SqliteConnection connection, CancellationToken shutdownToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completionSource.TrySetCanceled(cancellationToken);
                return;
            }

            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownToken);

            try
            {
                completionSource.TrySetResult(await operation(connection, linkedSource.Token));
            }
            catch (OperationCanceledException) when (linkedSource.IsCancellationRequested)
            {
                completionSource.TrySetCanceled(linkedSource.Token);
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
            }
        }

        public override void Fail(Exception exception) =>
            completionSource.TrySetException(exception);
    }
}
