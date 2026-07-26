using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Components;

public partial class StateRuleTransitionEditControl : UserControl
{
    public static readonly StyledProperty<StateAttributeEditViewModel?> SourceProperty =
        AvaloniaProperty.Register<StateRuleTransitionEditControl, StateAttributeEditViewModel?>(nameof(Source));

    public StateAttributeEditViewModel? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public StateRuleTransitionEditControl() =>
        AvaloniaXamlLoader.Load(this);
}
