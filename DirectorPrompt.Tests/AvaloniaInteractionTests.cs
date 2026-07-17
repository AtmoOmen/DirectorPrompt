using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Shapes;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Services;
using DirectorPrompt.ViewModels;
using DirectorPrompt.Views;
using DirectorPrompt.Views.Components;

namespace DirectorPrompt.Tests;

public sealed class AvaloniaInteractionTests
{
    [AvaloniaFact]
    public void PathComboBoxUsesNativeComboBoxTemplate()
    {
        var comboBox = new PathComboBox
        {
            ItemsSource = new[] { "Alpha", "Beta" },
            SelectedIndex = 0
        };
        var window = new Window { Content = comboBox };

        window.Show();

        Assert.True(comboBox.IsEffectivelyVisible);
        Assert.NotEmpty(comboBox.GetVisualDescendants());

        window.Close();
    }

    [AvaloniaFact]
    public void SettingsNavigationSwitchesVisiblePanel()
    {
        var window = new SettingsWindow();
        window.Show();
        var navigation = window.GetLogicalDescendants().OfType<ListBox>().First(control => control.Name == "NavList");
        var panels = window.GetLogicalDescendants()
            .OfType<StackPanel>()
            .Where(control => !string.IsNullOrEmpty(control.Name))
            .ToDictionary(control => control.Name!);

        navigation.SelectedIndex = 3;

        Assert.True(panels["TasksPanel"].IsVisible);
        Assert.False(panels["ProvidersPanel"].IsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void SettingsOthersPanelContainsLanSharingToggle()
    {
        var window = new SettingsWindow();

        Assert.NotNull(window.FindControl<ToggleSwitch>("LanSharingToggle"));
    }

    [AvaloniaFact]
    public void RemoteMainWindowUsesMobileLayout()
    {
        var viewModel = new MainViewModel
        (
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            new LanSharingStub()
        );
        var entry = new DialogEntryViewModel { Content = "Message" };
        viewModel.Dialog.Entries.Add(entry);
        var remoteWindow = new MainWindow(viewModel, false);
        var content      = Assert.IsAssignableFrom<Control>(remoteWindow.Content);
        remoteWindow.Content = null;
        content.DataContext  = viewModel;

        var host = new Window { Width = 390, Height = 700, Content = content };
        host.Show();
        Dispatcher.UIThread.RunJobs();

        string[] controlNames =
        [
            "MobileSessionsToggle",
            "EditProjectButton",
            "DetailsPanel",
            "MobileDetailsToggle",
            "MobileMoreActionsButton",
            "SessionSidebar",
            "MobileMessageRail",
            "DialogListBox"
        ];
        var controls = content.GetLogicalDescendants()
                              .OfType<Control>()
                              .Where(control => control.Name is not null && controlNames.Contains(control.Name))
                              .ToDictionary(static control => control.Name!);

        Assert.True(controls["MobileSessionsToggle"].IsVisible);
        Assert.False(controls["EditProjectButton"].IsVisible);
        Assert.False(controls["DetailsPanel"].IsVisible);
        Assert.Equal(1, Grid.GetColumn(controls["MobileMessageRail"]));

        ((ListBox)controls["DialogListBox"]).ScrollIntoView(entry);
        Dispatcher.UIThread.RunJobs();
        var messageMenuButton = content.GetVisualDescendants()
                                       .OfType<Button>()
                                       .First(button => button.Name == "MessageMenuButton");
        Assert.True(messageMenuButton.IsEnabled);
        messageMenuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(entry.IsMenuOpen);
        var messageActionsPanel = content.GetVisualDescendants()
                                         .OfType<Border>()
                                         .First(border => border.Name == "MessageActionsPanel");
        Assert.True(messageActionsPanel.IsHitTestVisible);
        Assert.Contains("open", messageActionsPanel.Classes);
        Assert.Same(messageActionsPanel.Parent, messageMenuButton.Parent);

        var sessionsWidth = controls["MobileSessionsToggle"].Bounds.Width;
        var actionsWidth  = controls["MobileMoreActionsButton"].Bounds.Width;
        var detailsWidth  = controls["MobileDetailsToggle"].Bounds.Width;
        Assert.InRange(Math.Abs(sessionsWidth - actionsWidth), 0, 1);
        Assert.InRange(Math.Abs(actionsWidth - detailsWidth), 0, 1);

        var detailsToggle = (ToggleButton)controls["MobileDetailsToggle"];
        detailsToggle.IsChecked = true;
        detailsToggle.RaiseEvent(new RoutedEventArgs(ToggleButton.ClickEvent));
        Assert.True(controls["DetailsPanel"].IsVisible);

        var sessionsToggle = (ToggleButton)controls["MobileSessionsToggle"];
        sessionsToggle.IsChecked = true;
        sessionsToggle.RaiseEvent(new RoutedEventArgs(ToggleButton.ClickEvent));
        Assert.True(controls["SessionSidebar"].IsVisible);

        sessionsToggle.IsChecked = false;
        sessionsToggle.RaiseEvent(new RoutedEventArgs(ToggleButton.ClickEvent));
        Assert.False(controls["SessionSidebar"].IsVisible);
        Assert.False(controls["DetailsPanel"].IsVisible);
        Assert.True(controls["MobileMessageRail"].IsVisible);
        Assert.False(sessionsToggle.IsChecked);

        host.Close();
    }

    [AvaloniaFact]
    public void ProjectNavigationSwitchesVisiblePanel()
    {
        var window = new ProjectEditWindow();
        window.Show();
        var navigation = window.GetLogicalDescendants().OfType<ListBox>().First(control => control.Name == "NavList");
        var panels = window.GetLogicalDescendants()
            .OfType<StackPanel>()
            .Where(control => !string.IsNullOrEmpty(control.Name))
            .ToDictionary(control => control.Name!);

        navigation.SelectedIndex = 2;

        Assert.True(panels["StatePanel"].IsVisible);
        Assert.False(panels["BasicPanel"].IsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void MessageRailRendersDotItems()
    {
        var rail = new MessageRail
        {
            Entries = new[]
            {
                new DialogEntryViewModel
                {
                    Type = EventType.NarrativeOutput,
                    Content = "Message"
                }
            }
        };
        var window = new Window { Content = rail };

        window.Show();

        Assert.Single(rail.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("indicator"));
        var listBox = rail.GetLogicalDescendants().OfType<ListBox>().First(control => control.Name == "RailListBox");
        Assert.Equal(ScrollBarVisibility.Hidden, ScrollViewer.GetVerticalScrollBarVisibility(listBox));

        window.Close();
    }

    [AvaloniaFact]
    public void ImportButtonOpensItsMenu()
    {
        var window = new MainWindow();
        window.Show();
        var button = window.GetLogicalDescendants().OfType<Button>().First(control => control.Name == "ImportButton");

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(button.ContextMenu?.IsOpen);

        window.Close();
    }

    [AvaloniaFact]
    public void CharacterAndMemoryFiltersRestoreDefaultSelections()
    {
        var characterPanel = new CharacterPanelViewModel();
        var memoryPanel = new MemoryPanelViewModel();
        var characterComboBox = new PathComboBox { DataContext = characterPanel };
        var sceneComboBox = new PathComboBox { DataContext = memoryPanel };
        var tagComboBox = new PathComboBox { DataContext = memoryPanel };
        characterComboBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CharacterPanelViewModel.AvailableCategories)));
        characterComboBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(CharacterPanelViewModel.SelectedCategory)) { Mode = BindingMode.TwoWay });
        sceneComboBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(MemoryPanelViewModel.AvailableScenes)));
        sceneComboBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(MemoryPanelViewModel.SelectedScene)) { Mode = BindingMode.TwoWay });
        tagComboBox.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(MemoryPanelViewModel.AvailableTags)));
        tagComboBox.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(MemoryPanelViewModel.SelectedTag)) { Mode = BindingMode.TwoWay });
        var window = new Window
        {
            Content = new StackPanel
            {
                Children =
                {
                    characterComboBox,
                    sceneComboBox,
                    tagComboBox
                }
            }
        };

        window.Show();
        characterPanel.SetGroups([]);
        memoryPanel.SetGroups([]);

        Assert.Same(characterPanel.AvailableCategories, characterComboBox.ItemsSource);
        Assert.Single(characterPanel.AvailableCategories);
        Assert.Equal(characterPanel.SelectedCategory, characterComboBox.SelectedItem);
        Assert.Equal(memoryPanel.SelectedScene, sceneComboBox.SelectedItem);
        Assert.Equal(memoryPanel.SelectedTag, tagComboBox.SelectedItem);

        window.Close();
    }

    private sealed class LanSharingStub : ILanSharingService
    {
        public Uri? Endpoint => null;

        public bool IsActive => false;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public Task ApplyAsync(bool enabled, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
