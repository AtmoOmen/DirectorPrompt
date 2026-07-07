using System.Windows;
using System.Windows.Input;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Components;

public partial class PhaseEditControl
{
    public static readonly DependencyProperty PhaseSourceProperty =
        DependencyProperty.Register
        (
            nameof(PhaseSource),
            typeof(StateAttributeEditViewModel),
            typeof(PhaseEditControl),
            new PropertyMetadata(null, OnPhaseSourceChanged)
        );

    public static readonly DependencyProperty AddPhaseCommandProperty =
        DependencyProperty.Register
        (
            nameof(AddPhaseCommand),
            typeof(ICommand),
            typeof(PhaseEditControl),
            new PropertyMetadata(null)
        );

    public static readonly DependencyProperty DeletePhaseCommandProperty =
        DependencyProperty.Register
        (
            nameof(DeletePhaseCommand),
            typeof(ICommand),
            typeof(PhaseEditControl),
            new PropertyMetadata(null)
        );

    public static readonly DependencyProperty AddPhaseKnowledgeCommandProperty =
        DependencyProperty.Register
        (
            nameof(AddPhaseKnowledgeCommand),
            typeof(ICommand),
            typeof(PhaseEditControl),
            new PropertyMetadata(null)
        );

    public static readonly DependencyProperty RemovePhaseKnowledgeCommandProperty =
        DependencyProperty.Register
        (
            nameof(RemovePhaseKnowledgeCommand),
            typeof(ICommand),
            typeof(PhaseEditControl),
            new PropertyMetadata(null)
        );

    public StateAttributeEditViewModel? PhaseSource
    {
        get => (StateAttributeEditViewModel?)GetValue(PhaseSourceProperty);
        set => SetValue(PhaseSourceProperty, value);
    }

    public ICommand? AddPhaseCommand
    {
        get => (ICommand?)GetValue(AddPhaseCommandProperty);
        set => SetValue(AddPhaseCommandProperty, value);
    }

    public ICommand? DeletePhaseCommand
    {
        get => (ICommand?)GetValue(DeletePhaseCommandProperty);
        set => SetValue(DeletePhaseCommandProperty, value);
    }

    public ICommand? AddPhaseKnowledgeCommand
    {
        get => (ICommand?)GetValue(AddPhaseKnowledgeCommandProperty);
        set => SetValue(AddPhaseKnowledgeCommandProperty, value);
    }

    public ICommand? RemovePhaseKnowledgeCommand
    {
        get => (ICommand?)GetValue(RemovePhaseKnowledgeCommandProperty);
        set => SetValue(RemovePhaseKnowledgeCommandProperty, value);
    }

    public PhaseEditControl() =>
        InitializeComponent();

    private static void OnPhaseSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PhaseEditControl control)
            control.RootPanel.DataContext = e.NewValue;
    }

    private void OnEditPhase(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PhaseEditViewModel phase })
            phase.IsEditing = !phase.IsEditing;
    }

    private void OnDeletePhase(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PhaseEditViewModel phase })
            return;

        if (!PromptDialog.Confirm(Window.GetWindow(this), Loc.Get("Common.Delete"), Loc.Get("Dialog.ConfirmDeletePhase", phase.Name), true))
            return;

        DeletePhaseCommand?.Execute(phase);
    }

    private void OnAddPhase(object sender, RoutedEventArgs e)
    {
        if (PhaseSource is null)
            return;

        AddPhaseCommand?.Execute(PhaseSource);
    }

    private void OnAddPhaseKnowledge(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PhaseEditViewModel phase })
            return;

        if (phase.SelectedAvailableItem is null)
            return;

        AddPhaseKnowledgeCommand?.Execute((phase, phase.SelectedAvailableItem));
    }

    private void OnRemovePhaseKnowledge(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe)
            return;

        if (fe.Tag is not PhaseEditViewModel phase)
            return;

        if (fe.DataContext is not KnowledgeSelectionItem item)
            return;

        RemovePhaseKnowledgeCommand?.Execute((phase, item));
    }
}
