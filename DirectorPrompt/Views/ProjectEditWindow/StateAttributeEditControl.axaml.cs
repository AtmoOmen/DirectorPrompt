using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views;

public partial class StateAttributeEditControl : UserControl
{
    public static readonly StyledProperty<ICommand?> DeleteCommandProperty =
        AvaloniaProperty.Register<StateAttributeEditControl, ICommand?>(nameof(DeleteCommand));

    public ICommand? DeleteCommand
    {
        get => GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

    public StateAttributeEditControl() =>
        AvaloniaXamlLoader.Load(this);

    private async void OnDeleteStateAttribute(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StateAttributeEditViewModel attr)
            return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        var confirmed = await PromptDialog.ConfirmAsync
                        (
                            owner,
                            Loc.Get("Common.Remove"),
                            Loc.Get("Dialog.ConfirmDeleteStateAttribute", attr.DisplayName),
                            true
                        );

        if (confirmed)
            DeleteCommand?.Execute(attr);
    }
}
