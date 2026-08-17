using Avalonia.Controls;
using Avalonia.Interactivity;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Settings;

public partial class PromptsSettingsView : UserControl
{
    public PromptsSettingsView() =>
        InitializeComponent();

    private async void OnRemovePrompt(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: PromptSettingViewModel prompt } ||
            DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        if (!await PromptDialog.ConfirmAsync
            (
                this,
                Loc.Get("Common.Remove"),
                Loc.Get("Dialog.ConfirmRemovePrompt", prompt.DisplayName),
                true
            ))
        {
            return;
        }

        viewModel.RemovePromptCommand.Execute(prompt);
    }
}