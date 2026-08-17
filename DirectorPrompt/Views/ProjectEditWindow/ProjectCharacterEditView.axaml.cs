using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views;

public partial class ProjectCharacterEditView : UserControl
{
    public ProjectEditViewModel? ViewModel => DataContext as ProjectEditViewModel;

    public ProjectCharacterEditView() =>
        AvaloniaXamlLoader.Load(this);

    private async void OnDeleteCharacterCategory(object sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: CharacterCategoryEditViewModel category } || ViewModel is null)
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteCharacterCategory", category.Name);

        if (!await PromptDialog.ConfirmAsync(this, Loc.Get("Common.Remove"), message, true))
            return;

        ViewModel.DeleteCharacterCategoryCommand.Execute(category);
    }

    private void OnAddCategoryStateAttribute(object sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: CharacterCategoryEditViewModel category })
            ViewModel?.AddCategoryStateAttributeCommand.Execute(category);
    }
}
