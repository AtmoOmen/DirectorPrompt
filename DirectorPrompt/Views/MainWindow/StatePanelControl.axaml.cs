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

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null)
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteDirective");

        if (await PromptDialog.ConfirmAsync(window, Loc.Get("Common.Remove"), message, true))
        {
            var viewModel = (MainViewModel)window.DataContext!;
            _ = viewModel.DeleteDirectiveCommand.ExecuteAsync(item);
        }
    }
}
