using Avalonia.Controls;
using Avalonia.Interactivity;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views;

public partial class MemoryPanelControl : UserControl
{
    public MemoryPanelControl() =>
        InitializeComponent();

    private void OnEditMemory(object sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: MemoryPanelItemViewModel item })
            item.StartEdit();
    }

    private async void OnDeleteMemory(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: MemoryPanelItemViewModel item })
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteMemory");

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Remove"), message, true))
            return;

        var viewModel = ViewModelLocator.GetMainViewModel(this);
        if (viewModel is null)
            return;

        _ = viewModel.DeleteMemoryCommand.ExecuteAsync(item);
    }

    private void OnSaveMemoryEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MemoryPanelItemViewModel item })
            return;

        var viewModel = ViewModelLocator.GetMainViewModel(this);
        if (viewModel is null)
            return;

        _ = viewModel.SaveMemoryEditCommand.ExecuteAsync(item);
    }

    private void OnCancelMemoryEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MemoryPanelItemViewModel item })
            return;

        item.CancelEdit();
    }
}