using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.LogicalTree;
using DirectorPrompt.Localization;

namespace DirectorPrompt.Views;

public partial class PromptDialog : Window
{
    private bool isInputMode;
    private bool boolResult;
    private string? stringResult;

    private TextBlock Message =>
        this.GetLogicalDescendants().OfType<TextBlock>().First(control => control.Name == "MessageText");

    private TextBox Input =>
        this.GetLogicalDescendants().OfType<TextBox>().First(control => control.Name == "InputBox");

    private Button Primary =>
        this.GetLogicalDescendants().OfType<Button>().First(control => control.Name == "PrimaryButton");

    private Button Secondary =>
        this.GetLogicalDescendants().OfType<Button>().First(control => control.Name == "SecondaryButton");

    public PromptDialog() =>
        AvaloniaXamlLoader.Load(this);

    private void OnPrimaryClick(object? sender, RoutedEventArgs e)
    {
        boolResult = true;

        if (isInputMode)
            stringResult = Input.Text;

        CloseResult();
    }

    private void OnSecondaryClick(object? sender, RoutedEventArgs e)
    {
        boolResult = false;
        stringResult = null;
        CloseResult();
    }

    private void OnInputBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Input.AcceptsReturn)
            return;

        boolResult = true;
        stringResult = Input.Text;
        CloseResult();
    }

    private void CloseResult()
    {
        if (Owner is not null)
            Close(isInputMode ? stringResult : boolResult);
        else
            Close();
    }

    private void Configure(string title, string message, string primaryText, string secondaryText, bool danger)
    {
        Title = title;
        Message.Text = message;
        Primary.Content = primaryText;
        Secondary.Content = secondaryText;

        if (!danger)
            Primary.Classes.Add("accent");
    }

    public static Task<bool> ConfirmAsync(Window? owner, string title, string message, bool danger = false) =>
        ConfirmAsync(owner, title, message, Loc.Get("Common.Delete"), Loc.Get("Common.Cancel"), danger);

    public static async Task<bool> ConfirmAsync
    (
        Window? owner,
        string title,
        string message,
        string primaryText,
        string secondaryText,
        bool danger = false
    )
    {
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

    public static async Task ShowErrorAsync(Window? owner, string title, string message)
    {
        await ConfirmAsync
        (
            owner,
            title,
            message,
            Loc.Get("Common.Close"),
            string.Empty
        );
    }

    private static async Task<string?> ShowInputAsync
    (
        Window? owner,
        string title,
        string prompt,
        string defaultValue,
        bool multiline
    )
    {
        var dialog = new PromptDialog();
        dialog.Configure(title, prompt, Loc.Get("Common.Save"), Loc.Get("Common.Cancel"), false);
        dialog.isInputMode = true;
        dialog.Input.Text = defaultValue;
        dialog.Input.PlaceholderText = prompt;
        dialog.Input.IsVisible = true;
        dialog.Message.IsVisible = false;
        dialog.Input.AcceptsReturn = multiline;
        dialog.Input.TextWrapping = multiline ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap;
        dialog.Input.MinHeight = multiline ? 120 : 0;
        dialog.Input.MaxHeight = multiline ? 300 : double.PositiveInfinity;
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
        Closed += (_, _) => completion.TrySetResult();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Show();
        return completion.Task;
    }
}
