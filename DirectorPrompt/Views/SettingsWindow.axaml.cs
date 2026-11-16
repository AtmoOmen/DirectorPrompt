using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DirectorPrompt.Services;
using DirectorPrompt.ViewModels;
using FluentAvalonia.UI.Windowing;

namespace DirectorPrompt.Views;

public partial class SettingsWindow : FAAppWindow, IRemoteDialogOwner
{
    private readonly SettingsViewModel viewModel;
    private          Action<bool>?     remoteCloseAction;

    public IRemoteDialogHost? RemoteDialogHost { get; set; }

    public SettingsWindow()
    {
        viewModel = null!;
        AvaloniaXamlLoader.Load(this);
    }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext    = viewModel;
        AvaloniaXamlLoader.Load(this);
    }

    internal void UseRemoteLayout() => RootLayout.Classes.Add("remote");

    internal void SetRemoteCloseAction(Action<bool>? action) =>
        remoteCloseAction = action;

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
        await viewModel.SaveCommand.ExecuteAsync(null);

        if (viewModel.SaveSuccess)
            Complete(true);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) =>
        Complete(false);
}
