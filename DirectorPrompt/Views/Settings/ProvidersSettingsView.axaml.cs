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
        if (sender is not Control { Tag: ProviderSettingViewModel provider })
            return;

        var window = TopLevel.GetTopLevel(this) as Window;

        if (window is null)
            return;

        if (!await PromptDialog.ConfirmAsync(window, Loc.Get("Common.Remove"), Loc.Get("Dialog.ConfirmRemoveProvider", provider.DisplayName), true))
            return;

        if (DataContext is SettingsViewModel viewModel)
            viewModel.RemoveProviderCommand.Execute(provider);
    }
}
