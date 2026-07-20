using Avalonia.Controls;
using Avalonia.Interactivity;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Settings;

public partial class ModelsSettingsView : UserControl
{
    public ModelsSettingsView() =>
        InitializeComponent();

    private async void OnRemoveModel(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: ModelSettingViewModel model })
            return;

        var window = TopLevel.GetTopLevel(this) as Window;

        if (window is null)
            return;

        if (!await PromptDialog.ConfirmAsync(window, Loc.Get("Common.Remove"), Loc.Get("Dialog.ConfirmRemoveModel", model.DisplayName), true))
            return;

        if (DataContext is SettingsViewModel viewModel)
            viewModel.RemoveModelCommand.Execute(model);
    }
}
