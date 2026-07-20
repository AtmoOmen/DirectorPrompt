using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Components;

public partial class PhaseEditControl : UserControl
{
    public static readonly StyledProperty<StateAttributeEditViewModel?> PhaseSourceProperty =
        AvaloniaProperty.Register<PhaseEditControl, StateAttributeEditViewModel?>(nameof(PhaseSource));

    public static readonly StyledProperty<ICommand?> AddPhaseCommandProperty =
        AvaloniaProperty.Register<PhaseEditControl, ICommand?>(nameof(AddPhaseCommand));

    public static readonly StyledProperty<ICommand?> DeletePhaseCommandProperty =
        AvaloniaProperty.Register<PhaseEditControl, ICommand?>(nameof(DeletePhaseCommand));

    public StateAttributeEditViewModel? PhaseSource
    {
        get => GetValue(PhaseSourceProperty);
        set => SetValue(PhaseSourceProperty, value);
    }

    public ICommand? AddPhaseCommand
    {
        get => GetValue(AddPhaseCommandProperty);
        set => SetValue(AddPhaseCommandProperty, value);
    }

    public ICommand? DeletePhaseCommand
    {
        get => GetValue(DeletePhaseCommandProperty);
        set => SetValue(DeletePhaseCommandProperty, value);
    }

    public PhaseEditControl() =>
        AvaloniaXamlLoader.Load(this);

    private void OnEditPhase(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: PhaseEditViewModel phase })
            phase.IsEditing = !phase.IsEditing;
    }

    private async void OnDeletePhase(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: PhaseEditViewModel phase })
            return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        var confirmed = await PromptDialog.ConfirmAsync
                        (
                            owner,
                            Loc.Get("Common.Remove"),
                            Loc.Get("Dialog.ConfirmDeletePhase", phase.Name),
                            true
                        );

        if (confirmed)
            DeletePhaseCommand?.Execute(phase);
    }

    private void OnAddPhase(object? sender, RoutedEventArgs e)
    {
        if (PhaseSource is not null)
            AddPhaseCommand?.Execute(PhaseSource);
    }
}
