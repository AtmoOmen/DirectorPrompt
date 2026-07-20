using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using DirectorPrompt.Localization;
using DirectorPrompt.Services;
using FluentAvalonia.UI.Windowing;

namespace DirectorPrompt.Views;

public partial class PromptDialog : FAAppWindow
{
    private bool             isInputMode;
    private bool             boolResult;
    private string?          stringResult;
    private Action<object?>? remoteCompletion;

    private TextBlock Message => MessageText;

    private TextBox Input => InputBox;

    private Button Primary => PrimaryButton;

    private Button Secondary => SecondaryButton;

    public PromptDialog()
    {
        AvaloniaXamlLoader.Load(this);
        MessageText     = this.FindControl<TextBlock>(nameof(MessageText))!;
        InputBox        = this.FindControl<TextBox>(nameof(InputBox))!;
        PrimaryButton   = this.FindControl<Button>(nameof(PrimaryButton))!;
        SecondaryButton = this.FindControl<Button>(nameof(SecondaryButton))!;
    }

    internal void SetRemoteCompletion(Action<object?>? completion) =>
        remoteCompletion = completion;

    internal Task<bool> ShowRemoteConfirmationAsync
    (
        string title,
        string message,
        string primaryText,
        string secondaryText,
        bool   danger
    )
    {
        Configure(title, message, primaryText, secondaryText, danger);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        remoteCompletion = result => completion.TrySetResult((bool)result!);
        return completion.Task;
    }

    internal Task<string?> ShowRemoteInputAsync
    (
        string title,
        string prompt,
        string defaultValue,
        bool   multiline
    )
    {
        Configure(title, prompt, Loc.Get("Common.Save"), Loc.Get("Common.Cancel"), false);
        isInputMode           = true;
        Input.Text            = defaultValue;
        Input.PlaceholderText = prompt;
        Input.IsVisible       = true;
        Message.IsVisible     = false;
        Input.AcceptsReturn   = multiline;
        Input.TextWrapping = multiline ?
                                 TextWrapping.Wrap :
                                 TextWrapping.NoWrap;
        Input.MinHeight = multiline ?
                              120 :
                              0;
        Input.MaxHeight = multiline ?
                              300 :
                              double.PositiveInfinity;

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        remoteCompletion = result => completion.TrySetResult((string?)result);
        Dispatcher.UIThread.Post
        (
            () =>
            {
                Input.Focus();
                Input.SelectAll();
            },
            DispatcherPriority.Input
        );
        return completion.Task;
    }

    private void OnPrimaryClick(object? sender, RoutedEventArgs e)
    {
        boolResult = true;

        if (isInputMode)
            stringResult = Input.Text;

        CloseResult();
    }

    private void OnSecondaryClick(object? sender, RoutedEventArgs e)
    {
        boolResult   = false;
        stringResult = null;
        CloseResult();
    }

    private void OnInputBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Input.AcceptsReturn)
            return;

        boolResult   = true;
        stringResult = Input.Text;
        CloseResult();
    }

    private void CloseResult()
    {
        if (remoteCompletion is { } completion)
        {
            remoteCompletion = null;
            completion
            (
                isInputMode ?
                    stringResult :
                    boolResult
            );
            return;
        }

        if (Owner is not null)
        {
            Close
            (
                isInputMode ?
                    stringResult :
                    boolResult
            );
        }
        else
            Close();
    }

    private void Configure(string title, string message, string primaryText, string secondaryText, bool danger)
    {
        Title             = title;
        Message.Text      = message;
        Primary.Content   = primaryText;
        Secondary.Content = secondaryText;

        if (!danger)
            Primary.Classes.Add("accent");
    }

    public static Task<bool> ConfirmAsync(Window? owner, string title, string message, bool danger = false) =>
        ConfirmAsync(owner, title, message, Loc.Get("Common.Remove"), Loc.Get("Common.Cancel"), danger);

    public static async Task<bool> ConfirmAsync
    (
        Window? owner,
        string  title,
        string  message,
        string  primaryText,
        string  secondaryText,
        bool    danger = false
    )
    {
        if (owner is IRemoteDialogOwner { RemoteDialogHost: { } remoteDialogHost })
        {
            return await remoteDialogHost.ShowConfirmationAsync
                   (
                       title,
                       message,
                       primaryText,
                       secondaryText,
                       danger
                   );
        }

        var dialog = new PromptDialog();
        dialog.Configure(title, message, primaryText, secondaryText, danger);

        if (owner is not null)
            return await dialog.ShowDialog<bool>(owner);

        await dialog.ShowStandaloneAsync();
        return dialog.boolResult;
    }

    public static Task<string?> InputAsync(Window? owner, string title, string prompt, string defaultValue) =>
        ShowInputAsync(owner, title, prompt, defaultValue, false);

    public static Task<string?> MultilineInputAsync(Window? owner, string title, string prompt, string defaultValue) =>
        ShowInputAsync(owner, title, prompt, defaultValue, true);

    public static async Task ShowErrorAsync(Window? owner, string title, string message) =>
        await ConfirmAsync
        (
            owner,
            title,
            message,
            Loc.Get("Common.Close"),
            string.Empty
        );

    private static async Task<string?> ShowInputAsync
    (
        Window? owner,
        string  title,
        string  prompt,
        string  defaultValue,
        bool    multiline
    )
    {
        if (owner is IRemoteDialogOwner { RemoteDialogHost: { } remoteDialogHost })
            return await remoteDialogHost.ShowInputAsync(title, prompt, defaultValue, multiline);

        var dialog = new PromptDialog();
        dialog.Configure(title, prompt, Loc.Get("Common.Save"), Loc.Get("Common.Cancel"), false);
        dialog.isInputMode           = true;
        dialog.Input.Text            = defaultValue;
        dialog.Input.PlaceholderText = prompt;
        dialog.Input.IsVisible       = true;
        dialog.Message.IsVisible     = false;
        dialog.Input.AcceptsReturn   = multiline;
        dialog.Input.TextWrapping = multiline ?
                                        TextWrapping.Wrap :
                                        TextWrapping.NoWrap;
        dialog.Input.MinHeight = multiline ?
                                     120 :
                                     0;
        dialog.Input.MaxHeight = multiline ?
                                     300 :
                                     double.PositiveInfinity;
        dialog.Opened += (_, _) =>
        {
            dialog.Input.Focus();
            dialog.Input.SelectAll();
        };

        if (owner is not null)
            return await dialog.ShowDialog<string?>(owner);

        await dialog.ShowStandaloneAsync();
        return dialog.stringResult;
    }

    private Task ShowStandaloneAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Closed                += (_, _) => completion.TrySetResult();
        WindowStartupLocation =  WindowStartupLocation.CenterScreen;
        Show();
        return completion.Task;
    }
}
