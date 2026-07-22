namespace DirectorPrompt.Services;

public interface INotificationService : IDisposable
{
    void NotifyInBackground(string title, string message, NotificationLevel level = NotificationLevel.Info);

    void NotifyInBackground(string title, string message, NotificationLevel level, string? context, params NotificationButton[] buttons);

    void Notify(string title, string message, NotificationLevel level = NotificationLevel.Info);

    void Notify(string title, string message, NotificationLevel level, string? context, params NotificationButton[] buttons);

    void RegisterActivationHandler(Action<NotificationActivationArgs> handler);
}
