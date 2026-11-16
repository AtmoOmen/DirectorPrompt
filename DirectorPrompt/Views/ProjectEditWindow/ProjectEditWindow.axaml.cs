using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DirectorPrompt.Services;
using DirectorPrompt.ViewModels;
using FluentAvalonia.UI.Windowing;

namespace DirectorPrompt.Views;

public partial class ProjectEditWindow : FAAppWindow, IRemoteDialogOwner
{
    private Action<bool>? remoteCloseAction;

    public ProjectEditViewModel ViewModel { get; }

    public IRemoteDialogHost? RemoteDialogHost { get; set; }

    public ProjectEditWindow()
    {
        ViewModel = null!;
        AvaloniaXamlLoader.Load(this);
    }

    public ProjectEditWindow(ProjectEditViewModel viewModel)
    {
        ViewModel   = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
    }

    internal void UseRemoteLayout() => RootLayout.Classes.Add("remote");

    internal void SetRemoteCloseAction(Action<bool>? action) =>
        remoteCloseAction = action;

    public void CloseWithoutSaving() =>
        Complete(false);

    private void Complete(bool result)
    {
        if (remoteCloseAction is { } action)
        {
            remoteCloseAction = null;
            action(result);
            return;
        }

        Close(result);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveCommand.ExecuteAsync(null);

        if (ViewModel.SaveSuccess)
            Complete(true);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) =>
        Complete(false);
}
