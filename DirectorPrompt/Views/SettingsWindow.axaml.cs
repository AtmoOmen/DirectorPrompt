using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.LogicalTree;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel viewModel;

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

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: ListBoxItem item })
            return;

        var tag = item.Tag as string;
        var panels = this.GetLogicalDescendants().OfType<StackPanel>().Where(panel => panel.Name is not null);

        foreach (var panel in panels)
        {
            panel.IsVisible = panel.Name switch
            {
                "ProvidersPanel" => tag == "providers",
                "ModelsPanel" => tag == "models",
                "PromptsPanel" => tag == "prompts",
                "TasksPanel" => tag == "tasks",
                "EmbeddingPanel" => tag == "embedding",
                "MemoryPanel" => tag == "memory",
                "RetrievalPanel" => tag == "retrieval",
                "OthersPanel" => tag == "others",
                _ => panel.IsVisible
            };
        }
    }

    private async void OnRemoveProvider(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: ProviderSettingViewModel provider })
            return;

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Remove"), Loc.Get("Dialog.ConfirmRemoveProvider", provider.DisplayName), true))
            return;

        viewModel.RemoveProviderCommand.Execute(provider);
    }

    private async void OnRemoveModel(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: ModelSettingViewModel model })
            return;

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Remove"), Loc.Get("Dialog.ConfirmRemoveModel", model.DisplayName), true))
            return;

        viewModel.RemoveModelCommand.Execute(model);
    }

    private async void OnRemovePrompt(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: PromptSettingViewModel prompt })
            return;

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Remove"), Loc.Get("Dialog.ConfirmRemovePrompt", prompt.DisplayName), true))
            return;

        viewModel.RemovePromptCommand.Execute(prompt);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        await viewModel.SaveCommand.ExecuteAsync(null);

        if (viewModel.SaveSuccess)
        {
            Close(true);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
