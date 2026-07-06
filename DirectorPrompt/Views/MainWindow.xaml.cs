﻿using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MenuItem = System.Windows.Controls.MenuItem;

namespace DirectorPrompt.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext    = viewModel;
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await viewModel.LoadProjectsCommand.ExecuteAsync(null);

    private void OnDirectiveTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox || viewModel is null)
            return;

        var index = comboBox.SelectedIndex;

        viewModel.DirectiveInput.SelectedType = index switch
        {
            0 => DirectiveType.Plot,
            1 => DirectiveType.Tone,
            2 => DirectiveType.TemporaryConstraint,
            3 => DirectiveType.SceneChange,
            _ => DirectiveType.Plot
        };
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(viewModel.DirectiveInput.InputContent))
        {
            viewModel.DirectiveInput.AddDirectiveCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnDeleteRound(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DialogEntryViewModel })
            _ = viewModel.DeleteLastRoundCommand.ExecuteAsync(null);
    }

    private void OnRewriteRound(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DialogEntryViewModel })
            _ = viewModel.RewriteLastRoundCommand.ExecuteAsync(null);
    }

    private void OnEditEntry(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: DialogEntryViewModel entry })
            entry.StartEdit();
    }

    private void OnMoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu is not null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.Placement       = PlacementMode.Bottom;
            element.ContextMenu.IsOpen          = true;
        }
    }

    private void OnEditProjectItem(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Project project })
            return;

        viewModel.CurrentProject = project;
        viewModel.EditProjectCommand.Execute(null);
    }

    private void OnDeleteProjectItem(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Project project })
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteProject", project.Name);
        var result  = MessageBox.Show(this, message, Loc.Get("Common.Delete"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        if (result == MessageBoxResult.OK)
            _ = viewModel.DeleteProjectCommand.ExecuteAsync(project);
    }

    private async void OnRenameSessionItem(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Session session })
            return;

        var newTitle = await ShowInputDialogAsync
        (
            Loc.Get("Dialog.RenameSessionTitle"),
            Loc.Get("Dialog.RenameSessionPrompt"),
            session.Title
        );

        if (newTitle is not null)
            _ = viewModel.RenameSessionAsync(session, newTitle);
    }

    private void OnDeleteSessionItem(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: Session session })
            return;

        var message = Loc.Get("Dialog.ConfirmDeleteSession", session.Title);
        var result  = MessageBox.Show(this, message, Loc.Get("Common.Delete"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        if (result == MessageBoxResult.OK)
            _ = viewModel.DeleteSessionCommand.ExecuteAsync(session);
    }

    private Task<string?> ShowInputDialogAsync(string title, string prompt, string defaultValue)
    {
        var dialog = new FluentWindow
        {
            Title                 = title,
            Width                 = 400,
            SizeToContent         = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner                 = this,
            ResizeMode            = ResizeMode.NoResize,
            ExtendsContentIntoTitleBar = true,
            WindowBackdropType    = WindowBackdropType.Mica,
            WindowCornerPreference = WindowCornerPreference.Round
        };

        var textBox = new Wpf.Ui.Controls.TextBox
        {
            Text            = defaultValue,
            Margin          = new Thickness(24, 8, 24, 16),
            PlaceholderText = prompt
        };

        textBox.Loaded += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

        var saveButton = new Wpf.Ui.Controls.Button
        {
            Content    = Loc.Get("Common.Save"),
            Appearance = ControlAppearance.Primary,
            Padding    = new Thickness(20, 6, 20, 6),
            Margin     = new Thickness(4, 0, 0, 0)
        };

        var cancelButton = new Wpf.Ui.Controls.Button
        {
            Content = Loc.Get("Common.Cancel"),
            Padding = new Thickness(20, 6, 20, 6),
            Margin  = new Thickness(4, 0, 24, 0)
        };

        var buttonPanel = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(0, 0, 0, 16)
        };

        buttonPanel.Children.Add(saveButton);
        buttonPanel.Children.Add(cancelButton);

        var titleBar = new TitleBar
        {
            Title = title,
            Margin = new Thickness(24, 8, 24, 0)
        };

        var panel = new StackPanel();
        panel.Children.Add(titleBar);
        panel.Children.Add(textBox);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        string? result = null;

        saveButton.Click += (_, _) =>
        {
            result = textBox.Text;
            dialog.DialogResult = true;
            dialog.Close();
        };

        cancelButton.Click += (_, _) =>
        {
            dialog.DialogResult = false;
            dialog.Close();
        };

        textBox.KeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == Key.Enter)
            {
                result = textBox.Text;
                dialog.DialogResult = true;
                dialog.Close();
            }
        };

        dialog.ShowDialog();

        return Task.FromResult(result);
    }
}
