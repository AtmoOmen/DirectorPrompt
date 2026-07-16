using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using DirectorPrompt.Views.Components;

namespace DirectorPrompt.Tests;

public sealed class SearchableComboBoxTests
{
    [AvaloniaFact]
    public void TextFiltersItemsAndSelectionUpdatesValue()
    {
        var control = new SearchableComboBox
        {
            DisplayMemberPath = nameof(TestOption.Name),
            SelectedValuePath = nameof(TestOption.ID),
            ItemsSource = new[]
            {
                new TestOption(1, "Alpha"),
                new TestOption(2, "Beta")
            }
        };

        control.Text = "bet";

        Assert.Single(control.FilteredItems);
        Assert.Equal("Beta", ((TestOption)control.FilteredItems[0]).Name);
    }

    [AvaloniaFact]
    public void InheritedDataContextLoadsDefaultTextAndSelection()
    {
        var source = new SearchSource
        {
            Options =
            [
                new TestOption(1, "Alpha"),
                new TestOption(2, "Beta")
            ],
            SelectedID = 2
        };
        var control = new SearchableComboBox
        {
            DisplayMemberPath = nameof(TestOption.Name),
            SelectedValuePath = nameof(TestOption.ID)
        };
        control.Bind(SearchableComboBox.ItemsSourceProperty, new Binding(nameof(SearchSource.Options)));
        control.Bind(SearchableComboBox.SelectedValueProperty, new Binding(nameof(SearchSource.SelectedID)));
        var window = new Window
        {
            DataContext = source,
            Content = control
        };

        window.Show();

        var textBox = control.GetLogicalDescendants().OfType<TextBox>().First(item => item.Name == "SearchBox");
        Assert.Equal("Beta", textBox.Text);

        window.Close();
    }

    private sealed class SearchSource
    {
        public required IReadOnlyList<TestOption> Options { get; init; }

        public int SelectedID { get; init; }
    }

    private sealed record TestOption(int ID, string Name);
}
