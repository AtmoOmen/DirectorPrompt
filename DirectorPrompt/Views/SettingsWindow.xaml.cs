using System.Windows;
using System.Windows.Controls;
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
        if (AgentsPanel is null || LanguagePanel is null)
            return;

        if (NavList.SelectedItem is not ListViewItem item)
            return;

        var tag = item.Tag as string;

        AgentsPanel.Visibility = tag == "agents" ?
                                     Visibility.Visible :
                                     Visibility.Collapsed;
        LanguagePanel.Visibility = tag == "language" ?
                                       Visibility.Visible :
                                       Visibility.Collapsed;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) =>
        await viewModel.SaveCommand.ExecuteAsync(null);

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        Close();
}
