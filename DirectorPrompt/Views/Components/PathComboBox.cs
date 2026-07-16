using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;

namespace DirectorPrompt.Views.Components;

public sealed class PathComboBox : ComboBox
{
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
        SelectedValueBinding = string.IsNullOrEmpty(SelectedValuePath) ? null : new Binding(SelectedValuePath);
}
