using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Threading;
using DirectorPrompt.Services;

namespace DirectorPrompt.Views.Components;

public sealed class PathComboBox : ComboBox
{
    private Popup? popup;
    private Control? remotePopupContent;

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
        if (!RemotePopupHost.IsRemote(this))
            return;

        HideRemoteDropdown();
    }

    private void ShowRemoteDropdown()
    {
        if (remotePopupContent is not null)
            return;

        popup = this.GetVisualDescendants().OfType<Popup>().FirstOrDefault();

        if (popup?.Child is not Control content)
            return;

        popup.Child       = null;
        remotePopupContent = content;

        if (!RemotePopupHost.Show(this, content, Bounds.Width, RestoreRemotePopupContent))
        {
            remotePopupContent = null;
            popup.Child         = content;
        }
    }

    private void HideRemoteDropdown()
    {
        if (remotePopupContent is null)
            return;

        var content = RemotePopupHost.Hide(this) ?? remotePopupContent;
        remotePopupContent = null;

        if (popup is not null)
            popup.Child = content;
    }

    private void RestoreRemotePopupContent(Control content)
    {
        remotePopupContent = null;

        if (popup is not null)
            popup.Child = content;
    }
}
