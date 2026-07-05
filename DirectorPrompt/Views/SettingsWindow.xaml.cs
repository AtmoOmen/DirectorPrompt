using System.Windows;
using System.Windows.Controls;
using DirectorPrompt.ViewModels;
using Wpf.Ui.Controls;

namespace DirectorPrompt.Views;

public partial class SettingsWindow : FluentWindow
{
    private readonly SettingsViewModel viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DatabasePanel is null)
        {
            return;
        }

        if (NavList.SelectedItem is not System.Windows.Controls.ListViewItem item)
        {
            return;
        }

        var tag = item.Tag as string;

        DatabasePanel.Visibility = tag == "database" ? Visibility.Visible : Visibility.Collapsed;
        AgentsPanel.Visibility = tag == "agents" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        await viewModel.SaveCommand.ExecuteAsync(null);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
