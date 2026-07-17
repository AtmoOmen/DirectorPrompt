using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DirectorPrompt.Services;

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
        AvaloniaProperty.Register<SearchableComboBox, object?>(nameof(SelectedValue), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SearchableComboBox, string>(nameof(Text), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> PlaceholderTextProperty =
        AvaloniaProperty.Register<SearchableComboBox, string>(nameof(PlaceholderText), string.Empty);

    private readonly List<object>              allItems = [];
    private          INotifyCollectionChanged? observedCollection;
    private          Border?                  remotePopupContent;
    private          ListBox?                 remoteResults;
    private          bool                      isUpdatingText;

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
        ItemsSourceProperty.Changed.AddClassHandler<SearchableComboBox>(static (control,       _) => control.ObserveItems());
        DisplayMemberPathProperty.Changed.AddClassHandler<SearchableComboBox>(static (control, _) => control.RefreshItems());
        SelectedValueProperty.Changed.AddClassHandler<SearchableComboBox>(static (control,     _) => control.UpdateDisplayText());
        TextProperty.Changed.AddClassHandler<SearchableComboBox>(static (control,              _) => control.UpdateTextFromProperty());
        PlaceholderTextProperty.Changed.AddClassHandler<SearchableComboBox>(static (control,   _) => control.SearchInput.PlaceholderText = control.PlaceholderText);
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

        isUpdatingText   = true;
        SearchInput.Text = Text;
        isUpdatingText   = false;
        FilterItems();
    }

    private void UpdateDisplayText()
    {
        if (isUpdatingText)
            return;

        var selectedItem = allItems.FirstOrDefault(item => Equals(GetValue(item, SelectedValuePath), SelectedValue));
        var display = selectedItem is null ?
                          Text :
                          GetDisplayValue(selectedItem);

        isUpdatingText   = true;
        SearchInput.Text = display;
        isUpdatingText   = false;
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
        SetDropDownOpen(FilteredItems.Count > 0 && SearchInput.IsFocused);
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

        Results.DisplayMemberBinding = new Binding(DisplayMemberPath);
    }

    private void OnSearchBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        SearchInput.SelectAll();
        FilterItems();
        SetDropDownOpen(FilteredItems.Count > 0);
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
        isUpdatingText       = true;
        SearchInput.Text     = display;
        Text                 = display;
        SelectedValue        = GetValue(selectedItem, SelectedValuePath);
        isUpdatingText       = false;
        Results.SelectedItem = null;
        SetDropDownOpen(false);
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && IsDropDownOpen && FilteredItems.Count > 0)
        {
            Results.Focus();
            Results.SelectedIndex = 0;
            e.Handled             = true;
        }
        else if (e.Key == Key.Escape)
        {
            SetDropDownOpen(false);
            UpdateDisplayText();
            e.Handled = true;
        }
    }

    private void OnResultKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SetDropDownOpen(false);
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

    private bool IsDropDownOpen =>
        RemotePopupHost.IsRemote(this) ?
            remotePopupContent is not null :
            DropDownPopup.IsOpen;

    private void SetDropDownOpen(bool value)
    {
        if (!RemotePopupHost.IsRemote(this))
        {
            DropDownPopup.IsOpen = value;
            return;
        }

        if (value)
            ShowRemoteDropdown();
        else
            HideRemoteDropdown();
    }

    private void ShowRemoteDropdown()
    {
        if (remotePopupContent is not null)
            return;

        var list = new ListBox
        {
            ItemsSource          = FilteredItems,
            DisplayMemberBinding = new Binding(DisplayMemberPath)
        };
        list.SelectionChanged += OnRemoteResultSelectionChanged;
        list.KeyDown          += OnRemoteResultKeyDown;

        var content = new Border
        {
            MinHeight       = 36,
            MaxHeight       = 320,
            Padding         = new Thickness(4),
            Background      = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(92, 92, 92)),
            BorderThickness = new Thickness(1),
            Child           = list
        };

        remoteResults      = list;
        remotePopupContent = content;

        if (!RemotePopupHost.Show(this, content, Bounds.Width, RestoreRemotePopupContent))
        {
            remotePopupContent = null;
            remoteResults      = null;
        }
    }

    private void HideRemoteDropdown()
    {
        if (remotePopupContent is null)
            return;

        RemotePopupHost.Hide(this);
        remotePopupContent = null;
        remoteResults      = null;
    }

    private void OnRemoteResultSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (remoteResults?.SelectedItem is { } selectedItem)
            CommitSelection(selectedItem);
    }

    private void OnRemoteResultKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SetDropDownOpen(false);
            SearchInput.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (remoteResults?.SelectedItem is { } selectedItem)
                CommitSelection(selectedItem);
            SearchInput.Focus();
            e.Handled = true;
        }
    }

    private void RestoreRemotePopupContent(Control content)
    {
        remotePopupContent = null;
        remoteResults      = null;
    }
}
