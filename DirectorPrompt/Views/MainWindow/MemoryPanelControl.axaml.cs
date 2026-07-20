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

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null)
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteMemory");

        if (await PromptDialog.ConfirmAsync(window, Loc.Get("Common.Remove"), message, true))
        {
            var viewModel = (MainViewModel)window.DataContext!;
            _ = viewModel.DeleteMemoryCommand.ExecuteAsync(item);
        }
    }
}
