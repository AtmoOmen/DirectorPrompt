#if RELEASE
using DirectorPrompt.Localization;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace DirectorPrompt.Update;

internal class UpdateOrchestrator
{
    private const string DISTRIBUTE_BASE_URL = "https://dp-distribute.atmoomen.top";

    public static async Task<(bool ShouldContinue, string? ErrorMessage)> RunAsync
    (
        Action<string>?             onStatus         = null,
        Action<int>?                onProgress       = null,
        Func<string, string, Task>? onChangelogReady = null
    )
    {
        try
        {
            var channel = GetUpdateChannel();

            if (channel is null)
            {
                Log.Information("当前平台暂不支持自动更新");
                return (true, null);
            }

            var updateSource = new SimpleWebSource(DISTRIBUTE_BASE_URL);

            var updateOptions = new UpdateOptions
            {
                ExplicitChannel       = channel,
                AllowVersionDowngrade = false
            };

            var updateManager = new UpdateManager(updateSource, updateOptions);

            onStatus?.Invoke(Loc.Get("Update.Checking"));

            var newRelease = await updateManager.CheckForUpdatesAsync();

            if (newRelease == null)
                return (true, null);

            onStatus?.Invoke(Loc.Get("Update.Downloading"));
            onProgress?.Invoke(0);

            await updateManager.DownloadUpdatesAsync
            (
                newRelease,
                progress =>
                {
                    onStatus?.Invoke(Loc.Get("Update.Downloading"));
                    onProgress?.Invoke(progress);
                }
            );

            var changelog = newRelease.TargetFullRelease.NotesMarkdown ?? string.Empty;
            var version   = newRelease.TargetFullRelease.Version.ToString();

            if (onChangelogReady is not null)
            {
                onStatus?.Invoke(Loc.Get("Update.Ready"));
                await onChangelogReady(changelog, version);
            }

            onStatus?.Invoke(Loc.Get("Update.Installing"));
            onProgress?.Invoke(100);

            updateManager.WaitExitThenApplyUpdates(newRelease, false, true, []);

            return (false, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "更新失败");

            var message = $"{Loc.Get("Update.FailedMessage", GetUpdateFailureMessage(ex))}{Environment.NewLine}{Environment.NewLine}{Loc.Get("Update.FailedHint")}";

            return (false, message);
        }
    }

    internal static string GetUpdateFailureMessage(Exception exception) =>
        exception switch
        {
            TimeoutException           => Loc.Get("Update.FailedTimeout"),
            OperationCanceledException => Loc.Get("Update.FailedCancelled"),
            _                          => exception.Message
        };

    private static string? GetUpdateChannel() =>
        OperatingSystem.IsWindows() ? "win" :
        OperatingSystem.IsLinux()   ? "linux" :
        null;
}
#endif
