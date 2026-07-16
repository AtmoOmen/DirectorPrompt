using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using DirectorPrompt.ViewModels;
using DirectorPrompt.Views;
using DirectorPrompt.Views.Components;

namespace DirectorPrompt.Tests;

public sealed class AvaloniaViewConstructionTests
{
    [AvaloniaFact]
    public void PrimaryViewsAndControlsConstruct()
    {
        Assert.NotNull(new MainWindow());
        Assert.NotNull(new ProjectEditWindow());
        Assert.NotNull(new SettingsWindow());
        Assert.NotNull(new PromptDialog());
        Assert.NotNull(new UpdateWindow());
        Assert.NotNull(new ChangelogWindow());
        Assert.NotNull(new DirectiveInputControl());
        Assert.NotNull(new PhaseEditControl());
        Assert.NotNull(new MessageRail());
    }

    [AvaloniaFact]
    public void AddPhaseButtonExecutesWindowCommand()
    {
        var viewModel = new ProjectEditViewModel
        (
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!
        );
        var attribute = new StateAttributeEditViewModel { IsEditing = true };
        var window    = new ProjectEditWindow(viewModel);

        viewModel.StateAttributes.Add(attribute);

        window.FindControl<StackPanel>("StatePanel")!.IsVisible = true;
        window.Show();

        var control = window.GetLogicalDescendants().OfType<PhaseEditControl>().First();
        var button  = control.GetLogicalDescendants().OfType<Button>().First();
        var phases  = control.FindControl<ItemsControl>("PhaseItems")!;

        Assert.Same(attribute.Phases, phases.ItemsSource);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Single(attribute.Phases);
        Assert.Contains(control.GetLogicalDescendants().OfType<Control>(), descendant => descendant.DataContext is PhaseEditViewModel);

        window.Close();
    }
}
