using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Controls.Shapes;
using DirectorPrompt.Domain.Enums;
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

        Assert.Single(rail.GetVisualDescendants().OfType<Ellipse>());
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
}
