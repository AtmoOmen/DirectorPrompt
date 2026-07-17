namespace DirectorPrompt.Services;

public interface IRemoteDialogHost
{
    Task<bool> ShowConfirmationAsync
    (
        string title,
        string message,
        string primaryText,
        string secondaryText,
        bool danger
    );

    Task<string?> ShowInputAsync
    (
        string title,
        string prompt,
        string defaultValue,
        bool multiline
    );
}

public interface IRemoteDialogOwner
{
    IRemoteDialogHost? RemoteDialogHost { get; set; }
}
