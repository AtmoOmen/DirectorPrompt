using Avalonia.Threading;
using Serilog;

namespace DirectorPrompt.Services;

public sealed class ProjectEditWindowCoordinator : IProjectEditWindowCoordinator
{
    private readonly Lock                              sync    = new();
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

            Log.Debug("已注册项目编辑窗口: 项目={ProjectID}, 窗口数={WindowCount}", projectID, projectWindows.Count);
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

            Log.Debug("已注销项目编辑窗口: 项目={ProjectID}, 剩余窗口数={WindowCount}", projectID, projectWindows.Count);
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
        {
            Log.Debug("项目外部变更无需关闭编辑窗口: 项目={ProjectID}", projectID);
            return;
        }

        Log.Information("因项目外部变更关闭编辑窗口: 项目={ProjectID}, 窗口数={WindowCount}", projectID, closeActions.Length);

        await Dispatcher.UIThread.InvokeAsync
        (() =>
            {
                foreach (var closeAction in closeActions)
                    closeAction();
            }
        );
    }
}
