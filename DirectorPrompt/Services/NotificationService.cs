using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.WinUI.Notifications;
using Serilog;

namespace DirectorPrompt.Services;

public sealed class NotificationService : IDisposable
{
    private Action<ActivationArgs>? activationHandler;

    private bool disposed;

    public NotificationService() =>
        ToastNotificationManagerCompat.OnActivated += OnNotificationActivated;

    public void NotifyInBackground(string title, string message, Level level = Level.Info) =>
        NotifyCore(title, message, level, null, null, true);

    public void NotifyInBackground(string title, string message, Level level, string? context, params Button[] buttons) =>
        NotifyCore(title, message, level, context, buttons, true);

    public void Notify(string title, string message, Level level = Level.Info) =>
        NotifyCore(title, message, level, null, null, false);

    public void Notify(string title, string message, Level level, string? context, params Button[] buttons) =>
        NotifyCore(title, message, level, context, buttons, false);

    public void RegisterActivationHandler(Action<ActivationArgs> handler) =>
        activationHandler = handler;

    public void Dispose()
    {
        if (disposed)
            return;

        disposed                                   =  true;
        ToastNotificationManagerCompat.OnActivated -= OnNotificationActivated;
        Log.Information("通知服务已释放");
    }

    private void NotifyCore
    (
        string    title,
        string    message,
        Level     level,
        string?   context,
        Button[]? buttons,
        bool      backgroundOnly
    )
    {
        if (disposed || (backgroundOnly && IsAnyWindowActive()))
        {
            Log.Debug
            (
                "通知已跳过: 服务已释放={Disposed}, 仅后台={BackgroundOnly}, 存在活动窗口={HasActiveWindow}",
                disposed,
                backgroundOnly,
                IsAnyWindowActive()
            );
            return;
        }

        Log.Information
        (
            "发送系统通知: 级别={Level}, 标题长度={TitleLength}, 内容长度={MessageLength}, 按钮数={ButtonCount}",
            level,
            title.Length,
            message.Length,
            buttons?.Length ?? 0
        );

        var builder = new ToastContentBuilder()
                      .AddText(title)
                      .AddText(message);

        if (level is Level.Warning or Level.Error)
            builder.SetToastDuration(ToastDuration.Long);

        if (!string.IsNullOrEmpty(context))
            builder.AddArgument("context", context);

        if (buttons is not null)
        {
            foreach (var button in buttons)
            {
                builder.AddButton
                (
                    new ToastButton()
                        .SetContent(button.Content)
                        .AddArgument("action", button.Arguments)
                );
            }
        }

        builder.Show();
        Log.Debug("系统通知已提交");
    }

    private static bool IsAnyWindowActive() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
        desktop.Windows.Any(window => window.IsActive);

    private void OnNotificationActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        var parsed = ToastArguments.Parse(args.Argument);
        var context = parsed.Contains("context") ?
                          parsed["context"] :
                          null;
        var action = parsed.Contains("action") ?
                         parsed["action"] :
                         null;

        Log.Information("收到系统通知激活: 有上下文={HasContext}, 有操作={HasAction}", context is not null, action is not null);

        Dispatcher.UIThread.Post
        (() =>
            {
                BringMainWindowToFront();
                activationHandler?.Invoke(new ActivationArgs(context, action));
            }
        );
    }

    private static void BringMainWindowToFront()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var window = desktop.MainWindow;

        if (window is null)
            return;

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Show();
        window.Activate();

        var hwnd = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;

        if (hwnd == nint.Zero)
            return;

        var foregroundHwnd     = GetForegroundWindow();
        var foregroundThreadID = GetWindowThreadProcessID(foregroundHwnd, nint.Zero);
        var currentThreadID    = GetCurrentThreadID();

        if (foregroundThreadID != currentThreadID)
        {
            AttachThreadInput(currentThreadID, foregroundThreadID, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(currentThreadID, foregroundThreadID, false);
        }
        else
            SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint GetWindowThreadProcessID(nint hWnd, nint ProcessID);

    [DllImport("user32.dll", EntryPoint = "AttachThreadInput")]
    private static extern bool AttachThreadInput(uint IDAttach, uint IDAttachTo, bool FAttach);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    private static extern uint GetCurrentThreadID();

    public enum Level
    {
        Info,
        Success,
        Warning,
        Error
    }

    public sealed record Button
    (
        string Content,
        string Arguments
    );

    public sealed record ActivationArgs
    (
        string? Context,
        string? Action
    );
}
