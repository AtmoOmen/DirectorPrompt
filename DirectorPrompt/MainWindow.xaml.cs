using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DirectorPrompt.ViewModels;
using Wpf.Ui.Controls;

namespace DirectorPrompt;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await viewModel.LoadProjectsCommand.ExecuteAsync(null);
    }

    private void OnDirectiveTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox || viewModel is null)
        {
            return;
        }

        var index = comboBox.SelectedIndex;

        viewModel.DirectiveInput.SelectedType = index switch
        {
            0 => Domain.Enums.DirectiveType.Plot,
            1 => Domain.Enums.DirectiveType.Tone,
            2 => Domain.Enums.DirectiveType.TemporaryConstraint,
            3 => Domain.Enums.DirectiveType.SceneChange,
            _ => Domain.Enums.DirectiveType.Plot
        };
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(viewModel.DirectiveInput.InputContent))
        {
            viewModel.DirectiveInput.AddDirectiveCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnDeleteRound(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: DialogEntryViewModel })
        {
            _ = viewModel.DeleteLastRoundCommand.ExecuteAsync(null);
        }
    }

    private void OnRewriteRound(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: DialogEntryViewModel })
        {
            _ = viewModel.RewriteLastRoundCommand.ExecuteAsync(null);
        }
    }
}
