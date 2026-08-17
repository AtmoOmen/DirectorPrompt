using Avalonia.Controls;
using Avalonia.Interactivity;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Settings;

public partial class ProvidersSettingsView : UserControl
{
    public ProvidersSettingsView() =>
        InitializeComponent();

    private async void OnRemoveProvider(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: ProviderSettingViewModel provider } ||
            DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        if (!await PromptDialog.ConfirmAsync
            (
                this,
                Loc.Get("Common.Remove"),
                Loc.Get("Dialog.ConfirmRemoveProvider", provider.DisplayName),
                true
            ))
        {
            return;
        }

        viewModel.RemoveProviderCommand.Execute(provider);
    }
}