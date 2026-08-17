using Avalonia.Controls;
using Avalonia.Interactivity;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views;

public partial class StatePanelControl : UserControl
{
    public StatePanelControl() =>
        InitializeComponent();

    private void OnEditDirective(object sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: DirectivePanelItemViewModel item })
            item.StartEdit();
    }

    private async void OnDeleteDirective(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: DirectivePanelItemViewModel item })
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteDirective");

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Remove"), message, true))
            return;

        var viewModel = ViewModelLocator.GetMainViewModel(this);
        if (viewModel is null)
            return;

        _ = viewModel.DeleteDirectiveCommand.ExecuteAsync(item);
    }

    private void OnSaveDirectiveEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DirectivePanelItemViewModel item })
            return;

        var viewModel = ViewModelLocator.GetMainViewModel(this);
        if (viewModel is null)
            return;

        _ = viewModel.SaveDirectiveEditCommand.ExecuteAsync(item);
    }

    private void OnCancelDirectiveEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DirectivePanelItemViewModel item })
            return;

        item.CancelEdit();
    }
}