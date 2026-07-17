using Avalonia.Threading;

namespace DirectorPrompt.Services;

public sealed class ProjectEditWindowCoordinator : IProjectEditWindowCoordinator
{
    private readonly Lock sync = new();
    private readonly Dictionary<long, HashSet<Action>> windows = [];

    public void Register(long projectID, Action closeWithoutSaving)
    {
        lock (sync)
        {
            if (!windows.TryGetValue(projectID, out var projectWindows))
            {
                projectWindows = [];
                windows.Add(projectID, projectWindows);
            }

            projectWindows.Add(closeWithoutSaving);
        }
    }

    public void Unregister(long projectID, Action closeWithoutSaving)
    {
        lock (sync)
        {
            if (!windows.TryGetValue(projectID, out var projectWindows))
                return;

            projectWindows.Remove(closeWithoutSaving);

            if (projectWindows.Count == 0)
                windows.Remove(projectID);
        }
    }

    public async Task CloseForExternalChangeAsync(long projectID)
    {
        Action[] closeActions;

        lock (sync)
        {
            closeActions = windows.TryGetValue(projectID, out var projectWindows) ?
                               [.. projectWindows] :
                               [];
        }

        if (closeActions.Length == 0)
            return;

        await Dispatcher.UIThread.InvokeAsync
        (
            () =>
            {
                foreach (var closeAction in closeActions)
                    closeAction();
            }
        );
    }
}
