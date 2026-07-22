using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.WinUI.Notifications;
using Serilog;

namespace DirectorPrompt.Services;

public sealed class WindowsNotificationService : INotificationService
{
    private Action<NotificationActivationArgs>? activationHandler;

    private bool disposed;

    public WindowsNotificationService() =>
        ToastNotificationManagerCompat.OnActivated += OnNotificationActivated;

    public void NotifyInBackground(string title, string message, NotificationLevel level = NotificationLevel.Info) =>
        NotifyCore(title, message, level, null, null, true);

    public void NotifyInBackground(string title, string message, NotificationLevel level, string? context, params NotificationButton[] buttons) =>
        NotifyCore(title, message, level, context, buttons, true);

    public void Notify(string title, string message, NotificationLevel level = NotificationLevel.Info) =>
        NotifyCore(title, message, level, null, null, false);

    public void Notify(string title, string message, NotificationLevel level, string? context, params NotificationButton[] buttons) =>
        NotifyCore(title, message, level, context, buttons, false);

    public void RegisterActivationHandler(Action<NotificationActivationArgs> handler) =>
        activationHandler = handler;

    public void Dispose()
    {
        if (disposed)
            return;

        disposed                                   =  true;
        ToastNotificationManagerCompat.OnActivated -= OnNotificationActivated;
        Log.Information("Windows 通知服务已释放");
    }

    private void NotifyCore
    (
        string                title,
        string                message,
        NotificationLevel     level,
        string?               context,
        NotificationButton[]? buttons,
        bool                  backgroundOnly
    )
    {
        var hasActiveWindow = DesktopWindowActivator.IsAnyWindowActive();

        if (disposed || (backgroundOnly && hasActiveWindow))
        {
            Log.Debug
            (
                "通知已跳过: 服务已释放={Disposed}, 仅后台={BackgroundOnly}, 存在活动窗口={HasActiveWindow}",
                disposed,
                backgroundOnly,
                hasActiveWindow
            );
            return;
        }

        Log.Information
        (
            "发送 Windows 系统通知: 级别={Level}, 标题长度={TitleLength}, 内容长度={MessageLength}, 按钮数={ButtonCount}",
            level,
            title.Length,
            message.Length,
            buttons?.Length ?? 0
        );

        var builder = new ToastContentBuilder()
                      .AddText(title)
                      .AddText(message);

        if (level is NotificationLevel.Warning or NotificationLevel.Error)
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
        Log.Debug("Windows 系统通知已提交");
    }

    private void OnNotificationActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        var parsed = ToastArguments.Parse(args.Argument);
        var context = parsed.Contains("context") ?
                          parsed["context"] :
                          null;
        var action = parsed.Contains("action") ?
                         parsed["action"] :
                         null;

        Log.Information("收到 Windows 通知激活: 有上下文={HasContext}, 有操作={HasAction}", context is not null, action is not null);

        Dispatcher.UIThread.Post
        (() =>
            {
                var window = DesktopWindowActivator.ActivateMainWindow();

                if (window is not null)
                    BringWindowToForeground(window);

                activationHandler?.Invoke(new NotificationActivationArgs(context, action));
            }
        );
    }

    private static void BringWindowToForeground(Window window)
    {
        var hwnd = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;

        if (hwnd == nint.Zero)
            return;

        var foregroundHwnd = GetForegroundWindow();

        if (foregroundHwnd == nint.Zero)
        {
            SetForegroundWindow(hwnd);
            return;
        }

        var foregroundThreadID = GetWindowThreadProcessID(foregroundHwnd, nint.Zero);
        var currentThreadID    = GetCurrentThreadID();

        if (foregroundThreadID != currentThreadID)
        {
            AttachThreadInput(currentThreadID, foregroundThreadID, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(currentThreadID, foregroundThreadID, false);
            return;
        }

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
}
