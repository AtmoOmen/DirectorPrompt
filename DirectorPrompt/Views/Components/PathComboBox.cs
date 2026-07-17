using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using DirectorPrompt.Services;

namespace DirectorPrompt.Views.Components;

public sealed class PathComboBox : ComboBox
{
    private Border? remotePopupContent;
    private ListBox? remoteList;
    private bool suppressDropDownClosed;

    protected override Type StyleKeyOverride => typeof(ComboBox);

    public static readonly StyledProperty<string> DisplayMemberPathProperty =
        AvaloniaProperty.Register<PathComboBox, string>(nameof(DisplayMemberPath), string.Empty);

    public static readonly StyledProperty<string> SelectedValuePathProperty =
        AvaloniaProperty.Register<PathComboBox, string>(nameof(SelectedValuePath), string.Empty);

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

    static PathComboBox()
    {
        DisplayMemberPathProperty.Changed.AddClassHandler<PathComboBox>(static (control, _) => control.UpdateDisplayTemplate());
        SelectedValuePathProperty.Changed.AddClassHandler<PathComboBox>(static (control, _) => control.UpdateSelectedValueBinding());
    }

    public PathComboBox()
    {
        DropDownOpened += OnDropDownOpened;
        DropDownClosed += OnDropDownClosed;
    }

    private void UpdateDisplayTemplate()
    {
        if (string.IsNullOrEmpty(DisplayMemberPath))
        {
            ItemTemplate = null;
            return;
        }

        var path = DisplayMemberPath;
        ItemTemplate = new FuncDataTemplate<object>
        ((_, _) =>
            {
                var textBlock = new TextBlock();
                textBlock.Bind(TextBlock.TextProperty, new Binding(path));
                return textBlock;
            }
        );
    }

    private void UpdateSelectedValueBinding() =>
        SelectedValueBinding = string.IsNullOrEmpty(SelectedValuePath) ?
                                   null :
                                   new Binding(SelectedValuePath);

    private void OnDropDownOpened(object? sender, EventArgs e)
    {
        if (!RemotePopupHost.IsRemote(this))
            return;

        Dispatcher.UIThread.Post(ShowRemoteDropdown, DispatcherPriority.Input);
    }

    private void OnDropDownClosed(object? sender, EventArgs e)
    {
        if (!RemotePopupHost.IsRemote(this) || suppressDropDownClosed)
            return;

        HideRemoteDropdown();
    }

    private void ShowRemoteDropdown()
    {
        if (remotePopupContent is not null)
            return;

        suppressDropDownClosed = true;
        IsDropDownOpen        = false;
        suppressDropDownClosed = false;

        var list = new ListBox
        {
            ItemTemplate = ItemTemplate
        };

        if (ItemsSource is not null)
        {
            list.ItemsSource  = ItemsSource;
            list.SelectedItem = SelectedItem;
        }
        else
        {
            list.ItemsSource = Items
                .Cast<object>()
                .Select(static item => item is ComboBoxItem comboItem ? comboItem.Content : item)
                .ToArray();
            list.SelectedIndex = SelectedIndex;
        }

        list.SelectionChanged += OnRemoteSelectionChanged;
        list.KeyDown          += OnRemoteListKeyDown;

        var content = new Border
        {
            MinHeight     = 36,
            MaxHeight     = 320,
            Padding       = new Thickness(4),
            Background    = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
            BorderBrush   = new SolidColorBrush(Color.FromRgb(92, 92, 92)),
            BorderThickness = new Thickness(1),
            Child         = list
        };

        remoteList          = list;
        remotePopupContent  = content;

        if (!RemotePopupHost.Show(this, content, Bounds.Width, RestoreRemotePopupContent))
        {
            remotePopupContent = null;
            remoteList         = null;
        }
    }

    private void HideRemoteDropdown()
    {
        if (remotePopupContent is null)
            return;

        RemotePopupHost.Hide(this);
        remotePopupContent = null;
        remoteList         = null;
    }

    private void OnRemoteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CommitRemoteSelection();
    }

    private void CommitRemoteSelection()
    {
        if (remoteList is null || remoteList.SelectedIndex < 0)
            return;

        if (ItemsSource is not null)
            SelectedItem = remoteList?.SelectedItem;
        else
            SelectedIndex = remoteList?.SelectedIndex ?? -1;

        HideRemoteDropdown();
    }

    private void RestoreRemotePopupContent(Control content)
    {
        remotePopupContent = null;
        remoteList         = null;
    }

    private void OnRemoteListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideRemoteDropdown();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (remoteList is not null)
                CommitRemoteSelection();
            e.Handled = true;
        }
    }
}
