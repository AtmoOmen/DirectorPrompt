using DirectorPrompt.Localization;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace DirectorPrompt.Update;

internal class UpdateOrchestrator
{
    private const string DISTRIBUTE_BASE_URL = "https://dp-distribute.atmoomen.top";

    public async Task<(bool ShouldContinue, string? ErrorMessage)> RunAsync
    (
        Action<string>? onStatus  = null,
        Action<int>?    onProgress = null
    )
    {
        try
        {
            var updateSource = new SimpleWebSource(DISTRIBUTE_BASE_URL);

            var updateOptions = new UpdateOptions
            {
                ExplicitChannel       = "win",
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

            onStatus?.Invoke(Loc.Get("Update.Installing"));
            onProgress?.Invoke(100);

            updateManager.ApplyUpdatesAndRestart(newRelease);

            return (false, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "更新失败");

            var message = $"{Loc.Get("Update.FailedMessage", GetUpdateFailureMessage(ex))}{Environment.NewLine}{Environment.NewLine}{Loc.Get("Update.FailedHint")}";

            return (true, message);
        }
    }

    internal static string GetUpdateFailureMessage(Exception exception) =>
        exception switch
        {
            TimeoutException           => Loc.Get("Update.FailedTimeout"),
            OperationCanceledException => Loc.Get("Update.FailedCancelled"),
            _                          => exception.Message
        };
}
