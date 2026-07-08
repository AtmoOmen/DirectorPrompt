using System.Windows;
using System.Threading.Tasks;
using DirectorPrompt.Localization;
using Wpf.Ui.Controls;

namespace DirectorPrompt.Views;

public partial class UpdateWindow : FluentWindow
{
    private TaskCompletionSource? closeTcs;

    public UpdateWindow()
    {
        InitializeComponent();
        WindowTitleBar.Title = Loc.Get("Update.Title");
    }

    public void UpdateStatus(string status) =>
        StatusText.Text = status;

    public void UpdateProgress(int progress)
    {
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value           = progress;
        ProgressText.Visibility      = Visibility.Visible;
        ProgressText.Text            = $"{progress}%";
    }

    public void ShowError(string message)
    {
        ProgressPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility    = Visibility.Visible;
        ErrorText.Text           = message;
        CloseButton.Content      = Loc.Get("Common.Close");
    }

    public Task WaitForCloseAsync()
    {
        closeTcs = new TaskCompletionSource();
        return closeTcs.Task;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        closeTcs?.TrySetResult();
}
