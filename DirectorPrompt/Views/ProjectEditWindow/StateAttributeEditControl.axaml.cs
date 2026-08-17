using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views;

public partial class StateAttributeEditControl : UserControl
{
    public StateAttributeEditControl() =>
        AvaloniaXamlLoader.Load(this);

    private async void OnDeleteStateAttribute(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StateAttributeEditViewModel attr)
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteStateAttribute", attr.DisplayName);

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Remove"), message, true))
            return;

        ViewModelLocator.GetProjectEditViewModel(this)?.DeleteStateAttributeCommand.Execute(attr);
    }
}