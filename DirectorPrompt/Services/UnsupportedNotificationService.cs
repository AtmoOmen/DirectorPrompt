using Serilog;

namespace DirectorPrompt.Services;

public sealed class UnsupportedNotificationService : INotificationService
{
    private bool disposed;

    public void NotifyInBackground(string title, string message, NotificationLevel level = NotificationLevel.Info) =>
        NotifyCore(title, message, level, true);

    public void NotifyInBackground(string title, string message, NotificationLevel level, string? context, params NotificationButton[] buttons) =>
        NotifyCore(title, message, level, true);

    public void Notify(string title, string message, NotificationLevel level = NotificationLevel.Info) =>
        NotifyCore(title, message, level, false);

    public void Notify(string title, string message, NotificationLevel level, string? context, params NotificationButton[] buttons) =>
        NotifyCore(title, message, level, false);

    public void RegisterActivationHandler(Action<NotificationActivationArgs> handler)
    {
        Log.Debug("当前平台暂不支持通知激活回调");
    }

    public void Dispose() =>
        disposed = true;

    private void NotifyCore(string title, string message, NotificationLevel level, bool backgroundOnly)
    {
        if (disposed || (backgroundOnly && DesktopWindowActivator.IsAnyWindowActive()))
            return;

        Log.Information
        (
            "当前平台暂不支持系统通知: 级别={Level}, 标题长度={TitleLength}, 内容长度={MessageLength}",
            level,
            title.Length,
            message.Length
        );
    }
}
