using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views;

public static class ViewModelLocator
{
    public static MainViewModel? GetMainViewModel(Visual visual) =>
        FindViewModel<MainViewModel>(visual);

    public static ProjectEditViewModel? GetProjectEditViewModel(Visual visual) =>
        FindViewModel<ProjectEditViewModel>(visual);

    private static T? FindViewModel<T>(Visual visual)
        where T : class =>
        visual.GetSelfAndVisualAncestors()
              .OfType<Control>()
              .Select(static control => control.DataContext)
              .OfType<T>()
              .FirstOrDefault();
}