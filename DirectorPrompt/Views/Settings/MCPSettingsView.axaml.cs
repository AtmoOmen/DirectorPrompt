using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DirectorPrompt.Localization;
using DirectorPrompt.Services;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Settings;

public partial class MCPSettingsView : UserControl
{
    public MCPSettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) =>
        CopyEndpointButton.IsVisible = !RemotePopupHost.IsRemote(this);

    private async void OnRemoveMCPServer(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: MCPServerSettingViewModel server } ||
            DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        if (!await PromptDialog.ConfirmAsync
            (
                this,
                Loc.Get("Settings.MCP.Title"),
                Loc.Get("Dialog.ConfirmRemoveMCPServer", server.DisplayName),
                true
            ))
        {
            return;
        }

        viewModel.RemoveMCPServerCommand.Execute(server);
    }

    private async void OnCopyInternalEndpoint(object sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;

        if (clipboard is null || DataContext is not SettingsViewModel viewModel)
            return;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(viewModel.InternalMCPEndpoint));
        await clipboard.SetDataAsync(transfer);
    }
}