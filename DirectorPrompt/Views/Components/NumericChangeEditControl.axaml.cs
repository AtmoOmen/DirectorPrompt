using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Components;

public partial class NumericChangeEditControl : UserControl
{
    public static readonly StyledProperty<StateAttributeEditViewModel?> SourceProperty =
        AvaloniaProperty.Register<NumericChangeEditControl, StateAttributeEditViewModel?>(nameof(Source));

    public static readonly StyledProperty<ICommand?> AddNumericChangeCommandProperty =
        AvaloniaProperty.Register<NumericChangeEditControl, ICommand?>(nameof(AddNumericChangeCommand));

    public static readonly StyledProperty<ICommand?> DeleteNumericChangeCommandProperty =
        AvaloniaProperty.Register<NumericChangeEditControl, ICommand?>(nameof(DeleteNumericChangeCommand));

    public StateAttributeEditViewModel? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public ICommand? AddNumericChangeCommand
    {
        get => GetValue(AddNumericChangeCommandProperty);
        set => SetValue(AddNumericChangeCommandProperty, value);
    }

    public ICommand? DeleteNumericChangeCommand
    {
        get => GetValue(DeleteNumericChangeCommandProperty);
        set => SetValue(DeleteNumericChangeCommandProperty, value);
    }

    public NumericChangeEditControl() =>
        AvaloniaXamlLoader.Load(this);

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        if (Source is not null)
            AddNumericChangeCommand?.Execute(Source);
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: NumericStateChangeRuleEditViewModel change })
            return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        var confirmed = await PromptDialog.ConfirmAsync
                        (
                            owner,
                            Loc.Get("Common.Delete"),
                            Loc.Get("Dialog.ConfirmDeleteNumericChange", change.Remarks),
                            true
                        );

        if (confirmed)
            DeleteNumericChangeCommand?.Execute(change);
    }

    private void OnClearAttribute(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: NumericStateChangeRuleEditViewModel change })
            change.AttributeName = null;
    }
}
