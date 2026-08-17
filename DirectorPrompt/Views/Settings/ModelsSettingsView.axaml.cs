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
        if (sender is not Control { Tag: ModelSettingViewModel model } ||
            DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        if (!await PromptDialog.ConfirmAsync
            (
                this,
                Loc.Get("Common.Remove"),
                Loc.Get("Dialog.ConfirmRemoveModel", model.DisplayName),
                true
            ))
        {
            return;
        }

        viewModel.RemoveModelCommand.Execute(model);
    }
}