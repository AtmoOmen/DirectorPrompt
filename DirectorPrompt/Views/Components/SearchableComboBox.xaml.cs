using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DirectorPrompt.Views.Components;

public sealed partial class SearchableComboBox : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register
        (
            nameof(ItemsSource),
            typeof(object),
            typeof(SearchableComboBox),
            new PropertyMetadata(null, OnItemsSourceChanged)
        );

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register
        (
            nameof(DisplayMemberPath),
            typeof(string),
            typeof(SearchableComboBox),
            new PropertyMetadata(string.Empty)
        );

    public static readonly DependencyProperty SelectedValuePathProperty =
        DependencyProperty.Register
        (
            nameof(SelectedValuePath),
            typeof(string),
            typeof(SearchableComboBox),
            new PropertyMetadata(string.Empty)
        );

    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register
        (
            nameof(SelectedValue),
            typeof(object),
            typeof(SearchableComboBox),
            new FrameworkPropertyMetadata
            (
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedValueChanged
            )
        );

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register
        (
            nameof(Text),
            typeof(string),
            typeof(SearchableComboBox),
            new FrameworkPropertyMetadata
            (
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextChanged
            )
        );

    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register
        (
            nameof(PlaceholderText),
            typeof(string),
            typeof(SearchableComboBox),
            new PropertyMetadata(string.Empty, OnPlaceholderTextChanged)
        );

    private bool         isUpdatingText;
    private List<object> allItems = [];

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public string SelectedValuePath
    {
        get => (string)GetValue(SelectedValuePathProperty);
        set => SetValue(SelectedValuePathProperty, value);
    }

    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public SearchableComboBox() =>
        InitializeComponent();

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SearchableComboBox control)
            return;

        if (e.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= control.OnCollectionChanged;

        control.CacheItems();
        control.UpdateDisplayText();

        if (e.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += control.OnCollectionChanged;
    }

    private static void OnSelectedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SearchableComboBox control)
            return;

        control.UpdateDisplayText();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SearchableComboBox control)
            return;

        if (control.isUpdatingText)
            return;

        control.SearchBox.Text = (string?)e.NewValue ?? string.Empty;
    }

    private static void OnPlaceholderTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SearchableComboBox control)
            return;

        control.SearchBox.PlaceholderText = (string?)e.NewValue ?? string.Empty;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        CacheItems();
        UpdateDisplayText();
    }

    private void CacheItems()
    {
        allItems.Clear();

        if (ItemsSource is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
                allItems.Add(item);
        }
    }

    private void UpdateDisplayText()
    {
        if (isUpdatingText)
            return;

        isUpdatingText = true;

        try
        {
            var displayText = string.Empty;

            if (!string.IsNullOrEmpty(SelectedValuePath) && SelectedValue is not null)
            {
                var selectedItem = FindItemByValue(SelectedValue);

                if (selectedItem is not null)
                    displayText = GetDisplayValue(selectedItem);
            }
            else
                displayText = Text;

            SearchBox.Text = displayText;
        }
        finally
        {
            isUpdatingText = false;
        }
    }

    private object? FindItemByValue(object? value)
    {
        if (value is null)
            return null;

        foreach (var item in allItems)
        {
            var itemValue = GetItemValue(item);

            if (itemValue is not null && itemValue.Equals(value))
                return item;
        }

        return null;
    }

    private string GetDisplayValue(object item)
    {
        if (string.IsNullOrEmpty(DisplayMemberPath))
            return item?.ToString() ?? string.Empty;

        var prop = item.GetType().GetProperty(DisplayMemberPath);

        return prop?.GetValue(item)?.ToString() ?? string.Empty;
    }

    private object? GetItemValue(object item)
    {
        if (string.IsNullOrEmpty(SelectedValuePath))
            return item;

        var prop = item.GetType().GetProperty(SelectedValuePath);

        return prop?.GetValue(item);
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (isUpdatingText)
            return;

        isUpdatingText = true;

        try
        {
            Text = SearchBox.Text;
        }
        finally
        {
            isUpdatingText = false;
        }

        FilterAndShowResults();
    }

    private void FilterAndShowResults()
    {
        var searchText = SearchBox.Text ?? string.Empty;

        var filtered = allItems.Where
        (item => GetDisplayValue(item).Contains(searchText, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        ResultsList.DisplayMemberPath = DisplayMemberPath;
        ResultsList.Items.Clear();

        foreach (var item in filtered)
            ResultsList.Items.Add(item);

        DropDown.IsOpen = filtered.Count > 0;
    }

    private void OnSearchBoxGotFocus(object sender, RoutedEventArgs e)
    {
        SearchBox.SelectAll();

        if (allItems.Count > 0 && !DropDown.IsOpen)
            FilterAndShowResults();
    }

    private void OnSearchBoxPreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (IsFocusInPopup(e.NewFocus as DependencyObject))
            return;

        DropDown.IsOpen = false;
    }

    private bool IsFocusInPopup(DependencyObject? focusTarget)
    {
        if (focusTarget is null)
            return false;

        if (ReferenceEquals(focusTarget, ResultsList))
            return true;

        if (focusTarget is ListBoxItem item)
            return ItemsControl.ItemsControlFromItemContainer(item) == ResultsList;

        return false;
    }

    private void OnDropDownOpened(object? sender, EventArgs e)
    {
        if (Window.GetWindow(this) is Window window)
            window.PreviewMouseDown += OnHostPreviewMouseDown;
    }

    private void OnDropDownClosed(object? sender, EventArgs e)
    {
        if (Window.GetWindow(this) is Window window)
            window.PreviewMouseDown -= OnHostPreviewMouseDown;

        UpdateDisplayText();
    }

    private void OnHostPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var posInControl  = e.GetPosition(this);
        var controlBounds = new Rect(0, 0, ActualWidth, ActualHeight);

        if (controlBounds.Contains(posInControl))
            return;

        if (DropDown.Child is FrameworkElement popupContent)
        {
            var posInPopup  = e.GetPosition(popupContent);
            var popupBounds = new Rect(0, 0, popupContent.ActualWidth, popupContent.ActualHeight);

            if (popupBounds.Contains(posInPopup))
                return;
        }

        DropDown.IsOpen = false;
    }

    private void OnResultSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is null)
            return;

        var selectedItem = ResultsList.SelectedItem;

        isUpdatingText = true;

        try
        {
            var displayValue = GetDisplayValue(selectedItem);

            SearchBox.Text = displayValue;
            Text           = displayValue;

            if (!string.IsNullOrEmpty(SelectedValuePath))
                SelectedValue = GetItemValue(selectedItem);

            DropDown.IsOpen = false;
        }
        finally
        {
            isUpdatingText           = false;
            ResultsList.SelectedItem = null;
        }
    }

    private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (DropDown.IsOpen && ResultsList.Items.Count > 0)
                {
                    ResultsList.Focus();

                    if (ResultsList.SelectedIndex < 0)
                        ResultsList.SelectedIndex = 0;
                }

                e.Handled = true;
                break;

            case Key.Escape:
                DropDown.IsOpen = false;
                UpdateDisplayText();
                e.Handled = true;
                break;

            case Key.Enter:
                if (DropDown.IsOpen && ResultsList.Items.Count > 0 && ResultsList.SelectedIndex < 0)
                    ResultsList.SelectedIndex = 0;

                break;

            case Key.Tab:
                DropDown.IsOpen = false;
                break;
        }
    }

    private void OnResultPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DropDown.IsOpen = false;
            UpdateDisplayText();
            SearchBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            DropDown.IsOpen = false;
            SearchBox.Focus();
            e.Handled = true;
        }
    }
}
