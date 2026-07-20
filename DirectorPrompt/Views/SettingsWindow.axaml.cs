using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using DirectorPrompt.Services;
using DirectorPrompt.ViewModels;
using DirectorPrompt.Views.Components;
using FluentAvalonia.UI.Windowing;

namespace DirectorPrompt.Views;

public partial class SettingsWindow : FAAppWindow, IRemoteDialogOwner
{
    private readonly SettingsViewModel viewModel;
    private          Action<bool>?     remoteCloseAction;

    private Grid         rootLayout        = null!;
    private PathComboBox remoteNavComboBox = null!;
    private ListBox      navList           = null!;

    public IRemoteDialogHost? RemoteDialogHost { get; set; }

    public SettingsWindow()
    {
        viewModel = null!;
        AvaloniaXamlLoader.Load(this);
        InitializeRemoteLayout();
    }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext    = viewModel;
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
                "ProvidersPanel" => tag == "providers",
                "ModelsPanel"    => tag == "models",
                "PromptsPanel"   => tag == "prompts",
                "TasksPanel"     => tag == "tasks",
                "MCPPanel"       => tag == "mcp",
                "EmbeddingPanel" => tag == "embedding",
                "MemoryPanel"    => tag == "memory",
                "RetrievalPanel" => tag == "retrieval",
                "OthersPanel"    => tag == "others",
                _                => panel.IsVisible
            };
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        await viewModel.SaveCommand.ExecuteAsync(null);

        if (viewModel.SaveSuccess)
            Complete(true);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) =>
        Complete(false);
}
