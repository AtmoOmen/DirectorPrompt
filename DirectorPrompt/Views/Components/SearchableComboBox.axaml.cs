using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.LogicalTree;

namespace DirectorPrompt.Views.Components;

public sealed partial class SearchableComboBox : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SearchableComboBox, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<string> DisplayMemberPathProperty =
        AvaloniaProperty.Register<SearchableComboBox, string>(nameof(DisplayMemberPath), string.Empty);

    public static readonly StyledProperty<string> SelectedValuePathProperty =
        AvaloniaProperty.Register<SearchableComboBox, string>(nameof(SelectedValuePath), string.Empty);

    public static readonly StyledProperty<object?> SelectedValueProperty =
        AvaloniaProperty.Register<SearchableComboBox, object?>(nameof(SelectedValue), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SearchableComboBox, string>(nameof(Text), string.Empty, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string> PlaceholderTextProperty =
        AvaloniaProperty.Register<SearchableComboBox, string>(nameof(PlaceholderText), string.Empty);

    private readonly List<object> allItems = [];
    private INotifyCollectionChanged? observedCollection;
    private bool isUpdatingText;

    private TextBox SearchInput =>
        this.GetLogicalDescendants().OfType<TextBox>().First(control => control.Name == "SearchBox");

    private Popup DropDownPopup =>
        this.GetLogicalDescendants().OfType<Popup>().First(control => control.Name == "DropDown");

    private ListBox Results =>
        this.GetLogicalDescendants().OfType<ListBox>().First(control => control.Name == "ResultsList");

    public ObservableCollection<object> FilteredItems { get; } = [];

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string DisplayMemberPath
    {
        get => GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public string SelectedValuePath
    {
        get => GetValue(SelectedValuePathProperty);
        set => SetValue(SelectedValuePathProperty, value);
    }

    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    static SearchableComboBox()
    {
        ItemsSourceProperty.Changed.AddClassHandler<SearchableComboBox>(static (control, _) => control.ObserveItems());
        DisplayMemberPathProperty.Changed.AddClassHandler<SearchableComboBox>(static (control, _) => control.RefreshItems());
        SelectedValueProperty.Changed.AddClassHandler<SearchableComboBox>(static (control, _) => control.UpdateDisplayText());
        TextProperty.Changed.AddClassHandler<SearchableComboBox>(static (control, _) => control.UpdateTextFromProperty());
        PlaceholderTextProperty.Changed.AddClassHandler<SearchableComboBox>(static (control, _) => control.SearchInput.PlaceholderText = control.PlaceholderText);
    }

    public SearchableComboBox()
    {
        AvaloniaXamlLoader.Load(this);
        Results.ItemsSource = FilteredItems;
    }

    private void ObserveItems()
    {
        if (observedCollection is not null)
            observedCollection.CollectionChanged -= OnCollectionChanged;

        observedCollection = ItemsSource as INotifyCollectionChanged;

        if (observedCollection is not null)
            observedCollection.CollectionChanged += OnCollectionChanged;

        RefreshItems();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshItems();

    private void RefreshItems()
    {
        allItems.Clear();

        if (ItemsSource is not null)
        {
            foreach (var item in ItemsSource)
            {
                if (item is not null)
                    allItems.Add(item);
            }
        }

        UpdateDisplayText();
        FilterItems();
    }

    private void UpdateTextFromProperty()
    {
        if (isUpdatingText || SearchInput.Text == Text)
            return;

        isUpdatingText = true;
        SearchInput.Text = Text;
        isUpdatingText = false;
        FilterItems();
    }

    private void UpdateDisplayText()
    {
        if (isUpdatingText)
            return;

        var selectedItem = allItems.FirstOrDefault(item => Equals(GetValue(item, SelectedValuePath), SelectedValue));
        var display = selectedItem is null ? Text : GetDisplayValue(selectedItem);

        isUpdatingText = true;
        SearchInput.Text = display;
        isUpdatingText = false;
    }

    private string GetDisplayValue(object item) =>
        GetValue(item, DisplayMemberPath)?.ToString() ?? string.Empty;

    private static object? GetValue(object item, string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return item;

        return TypeDescriptor.GetProperties(item)[propertyName]?.GetValue(item);
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (isUpdatingText)
            return;

        Text = SearchInput.Text ?? string.Empty;
        FilterItems();
        DropDownPopup.IsOpen = FilteredItems.Count > 0 && SearchInput.IsFocused;
    }

    private void FilterItems()
    {
        var searchText = SearchInput.Text ?? string.Empty;
        FilteredItems.Clear();

        foreach (var item in allItems)
        {
            if (GetDisplayValue(item).Contains(searchText, StringComparison.OrdinalIgnoreCase))
                FilteredItems.Add(item);
        }

        Results.DisplayMemberBinding = new Avalonia.Data.Binding(DisplayMemberPath);
    }

    private void OnSearchBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        SearchInput.SelectAll();
        FilterItems();
        DropDownPopup.IsOpen = FilteredItems.Count > 0;
    }

    private void OnResultSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Results.SelectedItem is not { } selectedItem)
            return;

        CommitSelection(selectedItem);
    }

    private void CommitSelection(object selectedItem)
    {
        var display = GetDisplayValue(selectedItem);
        isUpdatingText = true;
        SearchInput.Text = display;
        Text = display;
        SelectedValue = GetValue(selectedItem, SelectedValuePath);
        isUpdatingText = false;
        Results.SelectedItem = null;
        DropDownPopup.IsOpen = false;
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && DropDownPopup.IsOpen && FilteredItems.Count > 0)
        {
            Results.Focus();
            Results.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DropDownPopup.IsOpen = false;
            UpdateDisplayText();
            e.Handled = true;
        }
    }

    private void OnResultKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DropDownPopup.IsOpen = false;
            UpdateDisplayText();
            SearchInput.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (Results.SelectedItem is { } selectedItem)
                CommitSelection(selectedItem);
            SearchInput.Focus();
            e.Handled = true;
        }
    }
}
