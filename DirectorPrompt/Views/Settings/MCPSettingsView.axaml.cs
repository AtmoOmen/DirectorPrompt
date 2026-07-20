using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Settings;

public partial class MCPSettingsView : UserControl
{
    public MCPSettingsView() =>
        InitializeComponent();

    private async void OnRemoveMCPServer(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: MCPServerSettingViewModel server })
            return;

        var window = TopLevel.GetTopLevel(this) as Window;

        if (window is null)
            return;

        if (!await PromptDialog.ConfirmAsync(window, Loc.Get("Settings.MCP.Title"), Loc.Get("Dialog.ConfirmRemoveMCPServer", server.DisplayName), true))
            return;

        if (DataContext is SettingsViewModel viewModel)
            viewModel.RemoveMCPServerCommand.Execute(server);
    }

    private async void OnCopyInternalEndpoint(object sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;

        if (clipboard is not null && DataContext is SettingsViewModel viewModel)
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(viewModel.InternalMCPEndpoint));
            await clipboard.SetDataAsync(transfer);
        }
    }
}
