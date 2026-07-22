using Avalonia.Threading;
using DesktopNotifications;
using DesktopNotifications.FreeDesktop;
using Microsoft.Extensions.Hosting;
using Serilog;
using DesktopNotification = DesktopNotifications.Notification;

namespace DirectorPrompt.Services;

public sealed class LinuxNotificationService : INotificationService, IHostedService
{
    private readonly Lock notificationSync = new();
    private readonly Dictionary<DesktopNotification, string?> notificationContexts = [];

    private Action<NotificationActivationArgs>? activationHandler;
    private FreeDesktopNotificationManager? notificationManager;
    private bool disposed;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (disposed)
            return;

        FreeDesktopNotificationManager? manager = null;

        try
        {
            manager = new FreeDesktopNotificationManager(FreeDesktopApplicationContext.FromCurrentProcess());
            manager.NotificationActivated += OnNotificationActivated;
            manager.NotificationDismissed += OnNotificationDismissed;
            await manager.Initialize();
            notificationManager = manager;
            Log.Information("Linux 通知服务已连接到 FreeDesktop D-Bus");
        }
        catch (Exception ex)
        {
            manager?.Dispose();
            Log.Warning(ex, "无法连接 FreeDesktop 通知服务, 将跳过系统通知");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

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

        disposed = true;

        if (notificationManager is not null)
        {
            notificationManager.NotificationActivated -= OnNotificationActivated;
            notificationManager.NotificationDismissed -= OnNotificationDismissed;
            notificationManager.Dispose();
            notificationManager = null;
        }

        lock (notificationSync)
            notificationContexts.Clear();

        Log.Information("Linux 通知服务已释放");
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

        var manager = notificationManager;

        if (manager is null)
        {
            Log.Debug("Linux 通知服务尚未就绪, 已跳过通知");
            return;
        }

        var notification = new DesktopNotification
        {
            Title = title,
            Body  = message
        };

        if (buttons is not null)
        {
            foreach (var button in buttons)
                notification.Buttons.Add((button.Content, button.Arguments));
        }

        lock (notificationSync)
            notificationContexts[notification] = context;

        Log.Information
        (
            "发送 Linux 系统通知: 级别={Level}, 标题长度={TitleLength}, 内容长度={MessageLength}, 按钮数={ButtonCount}",
            level,
            title.Length,
            message.Length,
            buttons?.Length ?? 0
        );

        _ = ShowAsync(manager, notification);
    }

    private async Task ShowAsync(FreeDesktopNotificationManager manager, DesktopNotification notification)
    {
        try
        {
            await manager.ShowNotification(notification);
            Log.Debug("Linux 系统通知已提交");
        }
        catch (Exception ex)
        {
            lock (notificationSync)
                notificationContexts.Remove(notification);

            Log.Warning(ex, "发送 Linux 系统通知失败");
        }
    }

    private void OnNotificationActivated(object? sender, NotificationActivatedEventArgs args)
    {
        string? context;

        lock (notificationSync)
        {
            notificationContexts.TryGetValue(args.Notification, out context);
            notificationContexts.Remove(args.Notification);
        }

        Log.Information("收到 Linux 通知激活: 有上下文={HasContext}, 有操作={HasAction}", context is not null, args.ActionId is not null);

        Dispatcher.UIThread.Post
        (() =>
            {
                DesktopWindowActivator.ActivateMainWindow();
                activationHandler?.Invoke(new NotificationActivationArgs(context, args.ActionId));
            }
        );
    }

    private void OnNotificationDismissed(object? sender, NotificationDismissedEventArgs args)
    {
        lock (notificationSync)
            notificationContexts.Remove(args.Notification);
    }
}
