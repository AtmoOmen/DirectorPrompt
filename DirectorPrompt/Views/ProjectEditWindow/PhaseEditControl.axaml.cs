using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views;

public partial class PhaseEditControl : UserControl
{
    public static readonly StyledProperty<StateAttributeEditViewModel?> PhaseSourceProperty =
        AvaloniaProperty.Register<PhaseEditControl, StateAttributeEditViewModel?>(nameof(PhaseSource));

    public StateAttributeEditViewModel? PhaseSource
    {
        get => GetValue(PhaseSourceProperty);
        set => SetValue(PhaseSourceProperty, value);
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

        var message = Loc.Get("Dialog.ConfirmDeletePhase", phase.Name);

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Remove"), message, true))
            return;

        ViewModelLocator.GetProjectEditViewModel(this)?.DeletePhaseCommand.Execute(phase);
    }

    private void OnAddPhase(object? sender, RoutedEventArgs e)
    {
        if (PhaseSource is null)
            return;

        ViewModelLocator.GetProjectEditViewModel(this)?.AddPhaseCommand.Execute(PhaseSource);
    }
}