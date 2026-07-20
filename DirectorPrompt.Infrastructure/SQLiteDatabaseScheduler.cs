using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Serilog;

namespace DirectorPrompt.Infrastructure;

public sealed class SQLiteDatabaseScheduler : IAsyncDisposable
{
    private const int FOREGROUND_CAPACITY  = 1024;
    private const int MAINTENANCE_CAPACITY = 256;

    private readonly SQLiteConnectionFactory connectionFactory;
    private readonly Channel<WorkItem>       foregroundQueue;
    private readonly Channel<WorkItem>       maintenanceQueue;
    private readonly CancellationTokenSource shutdownSource = new();
    private readonly Task                    worker;

    private int foregroundPendingCount;
    private int maintenancePendingCount;

    public SQLiteDatabaseScheduler(SQLiteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
        foregroundQueue        = CreateQueue(FOREGROUND_CAPACITY);
        maintenanceQueue       = CreateQueue(MAINTENANCE_CAPACITY);
        worker                 = Task.Run(RunAsync);

        Log.Information
        (
            "SQLite 数据库调度器已创建: 前台队列容量={ForegroundCapacity}, 维护队列容量={MaintenanceCapacity}",
            FOREGROUND_CAPACITY,
            MAINTENANCE_CAPACITY
        );
    }

    public Task ExecuteAsync
    (
        Func<SqliteConnection, CancellationToken, Task> operation,
        SQLiteWorkPriority                              priority          = SQLiteWorkPriority.Foreground,
        CancellationToken                               cancellationToken = default,
        [CallerMemberName] string                       callerMemberName = "",
        [CallerFilePath] string                         callerFilePath   = ""
    ) =>
        ExecuteAsync
        (
            async (connection, token) =>
            {
                await operation(connection, token);
                return true;
            },
            priority,
            cancellationToken,
            callerMemberName,
            callerFilePath
        );

    public async Task<TResult> ExecuteAsync<TResult>
    (
        Func<SqliteConnection, CancellationToken, Task<TResult>> operation,
        SQLiteWorkPriority                                       priority          = SQLiteWorkPriority.Foreground,
        CancellationToken                                        cancellationToken = default,
        [CallerMemberName] string                                callerMemberName = "",
        [CallerFilePath] string                                  callerFilePath   = ""
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var operationName = GetOperationName(callerMemberName, callerFilePath);
        var item          = new WorkItem<TResult>(operation, cancellationToken, operationName, priority);
        var writer = priority == SQLiteWorkPriority.Foreground ?
                         foregroundQueue.Writer :
                         maintenanceQueue.Writer;

        var pendingCount = IncrementPendingCount(priority);

        try
        {
            await writer.WriteAsync(item, cancellationToken);
        }
        catch
        {
            DecrementPendingCount(priority);
            throw;
        }

        Log.Debug
        (
            "SQLite 写操作已入队: 操作={Operation}, 优先级={Priority}, 队列待处理={PendingCount}",
            operationName,
            priority,
            pendingCount
        );

        return await item.Task;
    }

    public Task ExecuteReadAsync
    (
        Func<SqliteConnection, CancellationToken, Task> operation,
        CancellationToken                               cancellationToken = default,
        [CallerMemberName] string                       callerMemberName = "",
        [CallerFilePath] string                         callerFilePath   = ""
    ) =>
        ExecuteReadAsync
        (
            async (connection, token) =>
            {
                await operation(connection, token);
                return true;
            },
            cancellationToken,
            callerMemberName,
            callerFilePath
        );

    public async Task<TResult> ExecuteReadAsync<TResult>
    (
        Func<SqliteConnection, CancellationToken, Task<TResult>> operation,
        CancellationToken                                        cancellationToken = default,
        [CallerMemberName] string                                callerMemberName = "",
        [CallerFilePath] string                                  callerFilePath   = ""
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var operationName = GetOperationName(callerMemberName, callerFilePath);
        var stopwatch     = Stopwatch.StartNew();

        Log.Debug("SQLite 读操作开始: 操作={Operation}", operationName);

        try
        {
            await using var connection = await connectionFactory.CreateAsync(true, cancellationToken);
            await ConfigureReadConnectionAsync(connection, cancellationToken);
            var result = await operation(connection, cancellationToken);

            Log.Debug
            (
                "SQLite 读操作完成: 操作={Operation}, 耗时={ElapsedMilliseconds}ms",
                operationName,
                stopwatch.ElapsedMilliseconds
            );

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log.Debug
            (
                "SQLite 读操作已取消: 操作={Operation}, 耗时={ElapsedMilliseconds}ms",
                operationName,
                stopwatch.ElapsedMilliseconds
            );
            throw;
        }
        catch (Exception exception)
        {
            Log.Error
            (
                exception,
                "SQLite 读操作失败: 操作={Operation}, 耗时={ElapsedMilliseconds}ms",
                operationName,
                stopwatch.ElapsedMilliseconds
            );
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Log.Information
        (
            "开始停止 SQLite 数据库调度器: 前台待处理={ForegroundPendingCount}, 维护待处理={MaintenancePendingCount}",
            Volatile.Read(ref foregroundPendingCount),
            Volatile.Read(ref maintenancePendingCount)
        );

        foregroundQueue.Writer.TryComplete();
        maintenanceQueue.Writer.TryComplete();

        await worker.ConfigureAwait(false);
        shutdownSource.Cancel();
        shutdownSource.Dispose();

        Log.Information("SQLite 数据库调度器已停止");
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
            await using var connection = await connectionFactory.CreateAsync(true, shutdownSource.Token);
            await ConfigureConnectionAsync(connection, shutdownSource.Token);

            Log.Information("SQLite 写入工作线程已启动");

            while (await WaitForWorkAsync())
            {
                if (!foregroundQueue.Reader.TryRead(out var item))
                    maintenanceQueue.Reader.TryRead(out item);

                if (item is not null)
                {
                    var pendingCount = DecrementPendingCount(item.Priority);

                    Log.Debug
                    (
                        "SQLite 写操作开始: 操作={Operation}, 优先级={Priority}, 队列待处理={PendingCount}",
                        item.OperationName,
                        item.Priority,
                        pendingCount
                    );
                    await item.ExecuteAsync(connection, shutdownSource.Token);
                }
            }

            Log.Information("SQLite 写入工作线程已停止");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "SQLite 写入工作线程异常停止");
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

            if (foregroundQueue.Reader.Completion.IsCompleted && maintenanceQueue.Reader.Completion.IsCompleted)
                return false;

            if (foregroundQueue.Reader.Completion.IsCompleted)
                return await maintenanceQueue.Reader.WaitToReadAsync(shutdownSource.Token);

            if (maintenanceQueue.Reader.Completion.IsCompleted)
                return await foregroundQueue.Reader.WaitToReadAsync(shutdownSource.Token);

            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdownSource.Token);
            var       foregroundWait   = foregroundQueue.Reader.WaitToReadAsync(waitCancellation.Token).AsTask();
            var       maintenanceWait  = maintenanceQueue.Reader.WaitToReadAsync(waitCancellation.Token).AsTask();
            var       completedWait    = await Task.WhenAny(foregroundWait, maintenanceWait);

            waitCancellation.Cancel();

            try
            {
                await Task.WhenAll(foregroundWait, maintenanceWait);
            }
            catch (OperationCanceledException) when (waitCancellation.IsCancellationRequested)
            {
            }

            if (await completedWait)
                return true;
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

    private static async Task ConfigureReadConnectionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GetOperationName(string callerMemberName, string callerFilePath)
    {
        var typeName = Path.GetFileNameWithoutExtension(callerFilePath);

        return string.IsNullOrEmpty(typeName) ?
                   callerMemberName :
                   $"{typeName}.{callerMemberName}";
    }

    private int IncrementPendingCount(SQLiteWorkPriority priority) =>
        priority == SQLiteWorkPriority.Foreground ?
            Interlocked.Increment(ref foregroundPendingCount) :
            Interlocked.Increment(ref maintenancePendingCount);

    private int DecrementPendingCount(SQLiteWorkPriority priority) =>
        priority == SQLiteWorkPriority.Foreground ?
            Interlocked.Decrement(ref foregroundPendingCount) :
            Interlocked.Decrement(ref maintenancePendingCount);

    private abstract class WorkItem
    {
        public abstract string OperationName { get; }

        public abstract SQLiteWorkPriority Priority { get; }

        public abstract Task ExecuteAsync(SqliteConnection connection, CancellationToken shutdownToken);

        public abstract void Fail(Exception exception);
    }

    private sealed class WorkItem<TResult>
    (
        Func<SqliteConnection, CancellationToken, Task<TResult>> operation,
        CancellationToken                                        cancellationToken,
        string                                                   operationName,
        SQLiteWorkPriority                                       priority
    ) : WorkItem
    {
        private readonly TaskCompletionSource<TResult> completionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TResult> Task => completionSource.Task;

        public override string OperationName => operationName;

        public override SQLiteWorkPriority Priority => priority;

        public override async Task ExecuteAsync(SqliteConnection connection, CancellationToken shutdownToken)
        {
            var stopwatch = Stopwatch.StartNew();

            if (cancellationToken.IsCancellationRequested)
            {
                completionSource.TrySetCanceled(cancellationToken);
                Log.Debug("SQLite 写操作在执行前已取消: 操作={Operation}", operationName);
                return;
            }

            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownToken);

            try
            {
                completionSource.TrySetResult(await operation(connection, linkedSource.Token));
                Log.Debug
                (
                    "SQLite 写操作完成: 操作={Operation}, 耗时={ElapsedMilliseconds}ms",
                    operationName,
                    stopwatch.ElapsedMilliseconds
                );
            }
            catch (OperationCanceledException) when (linkedSource.IsCancellationRequested)
            {
                completionSource.TrySetCanceled(linkedSource.Token);
                Log.Debug
                (
                    "SQLite 写操作已取消: 操作={Operation}, 耗时={ElapsedMilliseconds}ms",
                    operationName,
                    stopwatch.ElapsedMilliseconds
                );
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
                Log.Error
                (
                    ex,
                    "SQLite 写操作失败: 操作={Operation}, 耗时={ElapsedMilliseconds}ms",
                    operationName,
                    stopwatch.ElapsedMilliseconds
                );
            }
        }

        public override void Fail(Exception exception) =>
            completionSource.TrySetException(exception);
    }
}
