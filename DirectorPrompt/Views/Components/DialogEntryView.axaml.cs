using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Components;

public partial class DialogEntryView : UserControl
{
    public DialogEntryView() =>
        InitializeComponent();

    private void OnRollbackRound(object sender, RoutedEventArgs e)
    {
        if (DataContext is DialogEntryViewModel entry)
        {
            entry.IsMenuOpen = false;
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window is null)
                return;

            var mainViewModel = (MainViewModel)window.DataContext!;
            _ = mainViewModel.RollbackLastRoundCommand.ExecuteAsync(null);
        }
    }

    private async void OnCopyEntry(object sender, RoutedEventArgs e)
    {
        if (DataContext is DialogEntryViewModel entry)
        {
            var topLevel  = TopLevel.GetTopLevel(this);
            var clipboard = topLevel?.Clipboard;

            if (clipboard is not null)
            {
                var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.CreateText(entry.Content));
                await clipboard.SetDataAsync(transfer);
            }

            entry.IsMenuOpen = false;
        }
    }

    private void OnEditEntry(object sender, RoutedEventArgs e)
    {
        if (DataContext is DialogEntryViewModel entry)
        {
            entry.StartEdit();
            entry.IsMenuOpen = false;
        }
    }

    private void OnMoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DialogEntryViewModel entry)
        {
            entry.IsMenuOpen = !entry.IsMenuOpen;
            e.Handled        = true;
        }
    }
}
