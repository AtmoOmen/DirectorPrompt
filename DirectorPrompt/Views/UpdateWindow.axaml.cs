using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using DirectorPrompt.Localization;

namespace DirectorPrompt.Views;

public partial class UpdateWindow : Window
{
    private TaskCompletionSource? closeCompletion;

    private TextBlock Status =>
        this.GetLogicalDescendants().OfType<TextBlock>().First(control => control.Name == "StatusText");

    private StackPanel Progress =>
        this.GetLogicalDescendants().OfType<StackPanel>().First(control => control.Name == "ProgressPanel");

    private ProgressBar ProgressIndicator =>
        this.GetLogicalDescendants().OfType<ProgressBar>().First(control => control.Name == "ProgressBar");

    private TextBlock ProgressValue =>
        this.GetLogicalDescendants().OfType<TextBlock>().First(control => control.Name == "ProgressText");

    private StackPanel Error =>
        this.GetLogicalDescendants().OfType<StackPanel>().First(control => control.Name == "ErrorPanel");

    private TextBlock ErrorMessage =>
        this.GetLogicalDescendants().OfType<TextBlock>().First(control => control.Name == "ErrorText");

    private Button CloseAction =>
        this.GetLogicalDescendants().OfType<Button>().First(control => control.Name == "CloseButton");

    public UpdateWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Title = Loc.Get("Update.Title");
    }

    public void UpdateStatus(string status) =>
        Dispatcher.UIThread.Post(() => Status.Text = status);

    public void UpdateProgress(int progress) =>
        Dispatcher.UIThread.Post(() =>
        {
            ProgressIndicator.IsIndeterminate = false;
            ProgressIndicator.Value = progress;
            ProgressValue.IsVisible = true;
            ProgressValue.Text = $"{progress}%";
        });

    public void ShowError(string message)
    {
        Progress.IsVisible = false;
        Error.IsVisible = true;
        ErrorMessage.Text = message;
        CloseAction.Content = Loc.Get("Common.Close");
    }

    public Task WaitForCloseAsync()
    {
        closeCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return closeCompletion.Task;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) =>
        closeCompletion?.TrySetResult();
}
