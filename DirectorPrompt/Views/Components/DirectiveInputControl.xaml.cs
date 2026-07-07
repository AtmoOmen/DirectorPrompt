using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Components;

public partial class DirectiveInputControl
{
    public static readonly DependencyProperty DirectiveInputProperty =
        DependencyProperty.Register
        (
            nameof(DirectiveInput),
            typeof(DirectiveInputViewModel),
            typeof(DirectiveInputControl),
            new PropertyMetadata(null, OnDirectiveInputChanged)
        );

    public static readonly DependencyProperty ShowSendButtonProperty =
        DependencyProperty.Register
        (
            nameof(ShowSendButton),
            typeof(bool),
            typeof(DirectiveInputControl),
            new PropertyMetadata(false)
        );

    public static readonly DependencyProperty SendCommandProperty =
        DependencyProperty.Register
        (
            nameof(SendCommand),
            typeof(ICommand),
            typeof(DirectiveInputControl),
            new PropertyMetadata(null)
        );

    public static readonly DependencyProperty IsProcessingProperty =
        DependencyProperty.Register
        (
            nameof(IsProcessing),
            typeof(bool),
            typeof(DirectiveInputControl),
            new PropertyMetadata(false)
        );

    public DirectiveInputViewModel? DirectiveInput
    {
        get => (DirectiveInputViewModel?)GetValue(DirectiveInputProperty);
        set => SetValue(DirectiveInputProperty, value);
    }

    public bool ShowSendButton
    {
        get => (bool)GetValue(ShowSendButtonProperty);
        set => SetValue(ShowSendButtonProperty, value);
    }

    public ICommand? SendCommand
    {
        get => (ICommand?)GetValue(SendCommandProperty);
        set => SetValue(SendCommandProperty, value);
    }

    public bool IsProcessing
    {
        get => (bool)GetValue(IsProcessingProperty);
        set => SetValue(IsProcessingProperty, value);
    }

    public DirectiveInputControl() =>
        InitializeComponent();

    private static void OnDirectiveInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DirectiveInputControl control)
            control.RootPanel.DataContext = e.NewValue;
    }

    private void OnDirectiveTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        if (DirectiveInput is null)
            return;

        DirectiveInput.SelectedType = comboBox.SelectedIndex switch
        {
            1 => DirectiveType.Tone,
            2 => DirectiveType.TemporaryConstraint,
            3 => DirectiveType.SceneChange,
            _ => DirectiveType.Plot
        };
    }
}
