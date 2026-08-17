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

    public StateAttributeEditViewModel? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public NumericChangeEditControl() =>
        AvaloniaXamlLoader.Load(this);

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        if (Source is null)
            return;

        ViewModelLocator.GetProjectEditViewModel(this)?.AddNumericChangeCommand.Execute(Source);
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: NumericStateChangeRuleEditViewModel change })
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteNumericChange", change.Remarks);

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Remove"), message, true))
            return;

        ViewModelLocator.GetProjectEditViewModel(this)?.DeleteNumericChangeCommand.Execute(change);
    }
}