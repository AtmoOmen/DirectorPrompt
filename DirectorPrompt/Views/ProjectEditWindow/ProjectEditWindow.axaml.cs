using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using DirectorPrompt.Localization;
using DirectorPrompt.Services;
using DirectorPrompt.ViewModels;
using DirectorPrompt.Views.Components;
using FluentAvalonia.UI.Windowing;

namespace DirectorPrompt.Views;

public partial class ProjectEditWindow : FAAppWindow, IRemoteDialogOwner
{
    private Action<bool>? remoteCloseAction;

    private Grid         rootLayout        = null!;
    private PathComboBox remoteNavComboBox = null!;
    private ListBox      navList           = null!;

    public ProjectEditViewModel ViewModel { get; }

    public IRemoteDialogHost? RemoteDialogHost { get; set; }

    public ProjectEditWindow()
    {
        ViewModel = null!;
        AvaloniaXamlLoader.Load(this);
        InitializeRemoteLayout();
    }

    public ProjectEditWindow(ProjectEditViewModel viewModel)
    {
        ViewModel   = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
        InitializeRemoteLayout();
    }

    private void InitializeRemoteLayout()
    {
        rootLayout        = this.FindControl<Grid>(nameof(RootLayout))!;
        remoteNavComboBox = this.FindControl<PathComboBox>(nameof(RemoteNavComboBox))!;
        navList           = this.FindControl<ListBox>(nameof(NavList))!;
    }

    internal void UseRemoteLayout() => rootLayout.Classes.Add("remote");

    private void OnRemoteNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (navList is null || remoteNavComboBox is null)
            return;

        if (navList.SelectedIndex != remoteNavComboBox.SelectedIndex)
            navList.SelectedIndex = remoteNavComboBox.SelectedIndex;
    }

    internal void SetRemoteCloseAction(Action<bool>? action) =>
        remoteCloseAction = action;

    public void CloseWithoutSaving() =>
        Complete(false);

    private void Complete(bool result)
    {
        if (remoteCloseAction is { } action)
        {
            remoteCloseAction = null;
            action(result);
            return;
        }

        Close(result);
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: ListBoxItem item })
            return;

        var tag = item.Tag as string;
        if (sender is not Visual visual)
            return;

        var root = GetTopLevel(visual);

        if (root is null)
            return;

        var panels = root.GetVisualDescendants().OfType<Control>().Where(panel => panel.Name is not null);

        foreach (var panel in panels)
        {
            panel.IsVisible = panel.Name switch
            {
                "BasicPanel"     => tag == "basic",
                "KnowledgePanel" => tag == "knowledge",
                "StatePanel"     => tag == "state",
                "CharacterPanel" => tag == "character",
                _                => panel.IsVisible
            };
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveCommand.ExecuteAsync(null);

        if (ViewModel.SaveSuccess)
            Complete(true);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) =>
        Complete(false);
}
