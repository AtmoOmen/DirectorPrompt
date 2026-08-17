using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DirectorPrompt.Services;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Components;

public partial class DialogEntryView : UserControl
{
    public DialogEntryView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) =>
        CopyEntryButton.IsVisible = !RemotePopupHost.IsRemote(this);

    private void OnRollbackRound(object sender, RoutedEventArgs e)
    {
        if (DataContext is DialogEntryViewModel entry)
        {
            entry.IsMenuOpen = false;
            var viewModel = GetMainViewModel();
            if (viewModel is null)
                return;

            _ = viewModel.RollbackLastRoundCommand.ExecuteAsync(null);
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

    private void OnSaveEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DialogEntryViewModel entry })
            return;

        var viewModel = GetMainViewModel();
        if (viewModel is null)
            return;

        _ = viewModel.SaveEditCommand.ExecuteAsync(entry);
    }

    private void OnCancelEditClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DialogEntryViewModel entry)
            entry.CancelEdit();
    }

    private MainViewModel? GetMainViewModel() =>
        ViewModelLocator.GetMainViewModel(this);
}