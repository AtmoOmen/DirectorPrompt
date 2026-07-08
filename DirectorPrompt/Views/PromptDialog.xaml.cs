using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DirectorPrompt.Localization;
using Wpf.Ui.Controls;

namespace DirectorPrompt.Views;

public partial class PromptDialog : FluentWindow
{
    private bool   isInputMode;
    private string result = string.Empty;

    private PromptDialog() =>
        InitializeComponent();

    private void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        if (isInputMode)
            result = InputBox.Text;

        DialogResult = true;
        Close();
    }

    private void OnSecondaryClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnInputBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        result       = InputBox.Text;
        DialogResult = true;
        Close();
    }

    private void Configure(string title, string message, string primaryText, string secondaryText, ControlAppearance appearance)
    {
        Title                    = title;
        DialogTitleBar.Title     = title;
        MessageText.Text         = message;
        PrimaryButton.Content    = primaryText;
        PrimaryButton.Appearance = appearance;
        SecondaryButton.Content  = secondaryText;
    }

    public static bool Confirm(Window owner, string title, string message, bool danger = false) =>
        Confirm(owner, title, message, Loc.Get("Common.Delete"), Loc.Get("Common.Cancel"), danger);

    public static bool Confirm(Window owner, string title, string message, string primaryText, string secondaryText, bool danger = false)
    {
        var dialog = new PromptDialog();
        dialog.Configure
        (
            title,
            message,
            primaryText,
            secondaryText,
            danger ?
                ControlAppearance.Danger :
                ControlAppearance.Primary
        );
        dialog.Owner = owner;
        dialog.ShowDialog();

        return dialog.DialogResult == true;
    }

    public static string? Input(Window owner, string title, string prompt, string defaultValue)
    {
        var dialog = new PromptDialog();
        dialog.Configure
        (
            title,
            prompt,
            Loc.Get("Common.Save"),
            Loc.Get("Common.Cancel"),
            ControlAppearance.Primary
        );
        dialog.isInputMode              = true;
        dialog.InputBox.Text            = defaultValue;
        dialog.InputBox.PlaceholderText = prompt;
        dialog.InputBox.Visibility      = Visibility.Visible;
        dialog.MessageText.Visibility   = Visibility.Collapsed;
        dialog.InputBox.Loaded += (_, _) =>
        {
            dialog.InputBox.Focus();
            dialog.InputBox.SelectAll();
        };
        dialog.InputBox.KeyDown += dialog.OnInputBoxKeyDown;
        dialog.Owner            =  owner;
        dialog.ShowDialog();

        return dialog.DialogResult == true ?
                   dialog.result :
                   null;
    }

    public static string? MultilineInput(Window owner, string title, string prompt, string defaultValue)
    {
        var dialog = new PromptDialog();
        dialog.Configure
        (
            title,
            prompt,
            Loc.Get("Common.Save"),
            Loc.Get("Common.Cancel"),
            ControlAppearance.Primary
        );
        dialog.isInputMode                          = true;
        dialog.InputBox.Text                        = defaultValue;
        dialog.InputBox.PlaceholderText             = prompt;
        dialog.InputBox.Visibility                  = Visibility.Visible;
        dialog.InputBox.AcceptsReturn               = true;
        dialog.InputBox.TextWrapping                = TextWrapping.Wrap;
        dialog.InputBox.MinHeight                   = 120;
        dialog.InputBox.MaxHeight                   = 300;
        dialog.InputBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        dialog.MessageText.Visibility               = Visibility.Collapsed;
        dialog.InputBox.Loaded += (_, _) =>
        {
            dialog.InputBox.Focus();
            dialog.InputBox.SelectAll();
        };
        dialog.Owner = owner;
        dialog.ShowDialog();

        return dialog.DialogResult == true ?
                   dialog.result :
                   null;
    }
}
