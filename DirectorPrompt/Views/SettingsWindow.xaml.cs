using System.Windows;
using System.Windows.Controls;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;
using Wpf.Ui.Controls;
using ListViewItem = System.Windows.Controls.ListViewItem;

namespace DirectorPrompt.Views;

public partial class SettingsWindow : FluentWindow
{
    private readonly SettingsViewModel viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext    = viewModel;
        InitializeComponent();
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProvidersPanel is null ||
            ModelsPanel is null    ||
            PromptsPanel is null   ||
            TasksPanel is null     ||
            EmbeddingPanel is null ||
            MemoryPanel is null    ||
            RetrievalPanel is null ||
            LanguagePanel is null)
            return;

        if (NavList.SelectedItem is not ListViewItem item)
            return;

        var tag = item.Tag as string;

        ProvidersPanel.Visibility = tag == "providers" ?
                                        Visibility.Visible :
                                        Visibility.Collapsed;
        ModelsPanel.Visibility = tag == "models" ?
                                     Visibility.Visible :
                                     Visibility.Collapsed;
        PromptsPanel.Visibility = tag == "prompts" ?
                                      Visibility.Visible :
                                      Visibility.Collapsed;
        TasksPanel.Visibility = tag == "tasks" ?
                                    Visibility.Visible :
                                    Visibility.Collapsed;
        EmbeddingPanel.Visibility = tag == "embedding" ?
                                        Visibility.Visible :
                                        Visibility.Collapsed;
        MemoryPanel.Visibility = tag == "memory" ?
                                     Visibility.Visible :
                                     Visibility.Collapsed;
        RetrievalPanel.Visibility = tag == "retrieval" ?
                                        Visibility.Visible :
                                        Visibility.Collapsed;
        LanguagePanel.Visibility = tag == "language" ?
                                       Visibility.Visible :
                                       Visibility.Collapsed;
    }

    private void OnRemoveProvider(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ProviderSettingViewModel provider })
            return;

        if (!PromptDialog.Confirm(this, Loc.Get("Common.Remove"), Loc.Get("Dialog.ConfirmRemoveProvider", provider.DisplayName), true))
            return;

        viewModel.RemoveProviderCommand.Execute(provider);
    }

    private void OnRemoveModel(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ModelSettingViewModel model })
            return;

        if (!PromptDialog.Confirm(this, Loc.Get("Common.Remove"), Loc.Get("Dialog.ConfirmRemoveModel", model.DisplayName), true))
            return;

        viewModel.RemoveModelCommand.Execute(model);
    }

    private void OnRemovePrompt(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PromptSettingViewModel prompt })
            return;

        if (!PromptDialog.Confirm(this, Loc.Get("Common.Remove"), Loc.Get("Dialog.ConfirmRemovePrompt", prompt.DisplayName), true))
            return;

        viewModel.RemovePromptCommand.Execute(prompt);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        await viewModel.SaveCommand.ExecuteAsync(null);

        if (viewModel.SaveSuccess)
        {
            DialogResult = true;
            Close();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
