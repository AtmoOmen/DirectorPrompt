using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Components;

public partial class DirectiveInputControl : UserControl
{
    public static readonly StyledProperty<DirectiveInputViewModel?> DirectiveInputProperty =
        AvaloniaProperty.Register<DirectiveInputControl, DirectiveInputViewModel?>(nameof(DirectiveInput));

    public static readonly StyledProperty<bool> ShowSendButtonProperty =
        AvaloniaProperty.Register<DirectiveInputControl, bool>(nameof(ShowSendButton));

    public static readonly StyledProperty<ICommand?> SendCommandProperty =
        AvaloniaProperty.Register<DirectiveInputControl, ICommand?>(nameof(SendCommand));

    public static readonly StyledProperty<bool> IsProcessingProperty =
        AvaloniaProperty.Register<DirectiveInputControl, bool>(nameof(IsProcessing));

    public DirectiveInputViewModel? DirectiveInput
    {
        get => GetValue(DirectiveInputProperty);
        set => SetValue(DirectiveInputProperty, value);
    }

    public bool ShowSendButton
    {
        get => GetValue(ShowSendButtonProperty);
        set => SetValue(ShowSendButtonProperty, value);
    }

    public ICommand? SendCommand
    {
        get => GetValue(SendCommandProperty);
        set => SetValue(SendCommandProperty, value);
    }

    public bool IsProcessing
    {
        get => GetValue(IsProcessingProperty);
        set => SetValue(IsProcessingProperty, value);
    }

    private Panel Root =>
        this.GetLogicalDescendants().OfType<Panel>().First(control => control.Name == "RootPanel");

    static DirectiveInputControl() =>
        DirectiveInputProperty.Changed.AddClassHandler<DirectiveInputControl>
            (static (control, _) => control.Root.DataContext = control.DirectiveInput);

    public DirectiveInputControl() =>
        AvaloniaXamlLoader.Load(this);

    private void OnDirectiveTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox || DirectiveInput is null)
            return;

        DirectiveInput.SelectedType = comboBox.SelectedIndex switch
        {
            1 => DirectiveType.Constraint,
            2 => DirectiveType.SceneChange,
            _ => DirectiveType.Plot
        };
    }
}
