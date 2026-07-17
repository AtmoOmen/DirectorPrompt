using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Localization;
using DirectorPrompt.ViewModels;
using DirectorPrompt.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DirectorPrompt.Services;

public sealed class RemoteWindowService
(
    IServiceProvider   serviceProvider,
    UserSettings       userSettings,
    ILanSharingService lanSharingService,
    IProjectEditWindowCoordinator? projectEditWindowCoordinator = null
) : IWindowService, IRemoteDialogHost
{
    private readonly List<Control> openWindows = [];

    private Panel? overlay;
    private Canvas? popupLayer;

    public void Attach(Panel overlay, Canvas popupLayer)
    {
        this.overlay   = overlay;
        this.popupLayer = popupLayer;
        overlay.IsVisible = true;
        RemotePopupHost.Attach(popupLayer);
    }

    public void Detach()
    {
        if (popupLayer is not null)
            RemotePopupHost.Detach(popupLayer);

        if (overlay is not null)
            overlay.Children.Clear();

        openWindows.Clear();
        overlay    = null;
        popupLayer = null;
    }

    public Task<string?> InputAsync(string title, string prompt, string defaultValue) =>
        ShowInputAsync(title, prompt, defaultValue, false);

    public async Task<bool> EditProjectAsync(Project project)
    {
        var window = serviceProvider.GetRequiredService<ProjectEditWindow>();
        await window.ViewModel.LoadFromProjectAsync(project);

        window.RemoteDialogHost = this;
        projectEditWindowCoordinator?.Register(project.ID, window.CloseWithoutSaving);

        try
        {
            return await ShowWindowAsync<bool>
            (
                window,
                completion => window.SetRemoteCloseAction(completion)
            );
        }
        finally
        {
            projectEditWindowCoordinator?.Unregister(project.ID, window.CloseWithoutSaving);
        }
    }

    public async Task ShowSettingsAsync()
    {
        var window = serviceProvider.GetRequiredService<SettingsWindow>();
        window.RemoteDialogHost = this;

        var saved = await ShowWindowAsync<bool>
        (
            window,
            completion => window.SetRemoteCloseAction(completion)
        );

        if (!saved)
            return;

        try
        {
            await lanSharingService.ApplyAsync(userSettings.RemoteControl.IsLanSharingEnabled);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync
            (
                Loc.Get("Settings.Title"),
                Loc.Get("Settings.SaveFailed", ex.Message)
            );
        }
    }

    public Task<bool> ShowConfirmationAsync
    (
        string title,
        string message,
        string primaryText,
        string secondaryText,
        bool danger
    )
    {
        var dialog = new PromptDialog();
        var completion = dialog.ShowRemoteConfirmationAsync
        (
            title,
            message,
            primaryText,
            secondaryText,
            danger
        );

        return ShowPromptAsync(dialog, completion);
    }

    public Task<string?> ShowInputAsync
    (
        string title,
        string prompt,
        string defaultValue,
        bool multiline
    )
    {
        var dialog = new PromptDialog();
        var completion = dialog.ShowRemoteInputAsync(title, prompt, defaultValue, multiline);

        return ShowPromptAsync(dialog, completion);
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        await ShowConfirmationAsync
        (
            title,
            message,
            Loc.Get("Common.Close"),
            string.Empty,
            false
        );
    }

    private async Task<TResult> ShowWindowAsync<TResult>
    (
        Window window,
        Action<Action<TResult>> setCompletion
    )
    {
        if (window is SettingsWindow settingsWindow)
            settingsWindow.UseRemoteLayout();
        else if (window is ProjectEditWindow projectEditWindow)
            projectEditWindow.UseRemoteLayout();

        var content = DetachContent(window);
        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        setCompletion(result => completion.TrySetResult(result));
        var modal = CreateModal(window, content);
        AddWindow(window, modal);

        try
        {
            return await completion.Task;
        }
        finally
        {
            RemoveWindow(window, modal, content);
        }
    }

    private async Task<TResult> ShowPromptAsync<TResult>
    (
        PromptDialog dialog,
        Task<TResult> completion
    )
    {
        var content = DetachContent(dialog);
        var modal = CreateModal(dialog, content);
        AddWindow(dialog, modal);

        try
        {
            return await completion;
        }
        finally
        {
            RemoveWindow(dialog, modal, content);
        }
    }

    private Control DetachContent(Window window)
    {
        if (window.Content is not Control content)
            throw new InvalidOperationException($"{window.GetType().Name} 内容无法用于远程显示");

        window.Content = null;
        content.DataContext = window.DataContext;
        return content;
    }

    private Control CreateModal(Window window, Control content)
    {
        var modal = new Grid
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment   = Avalonia.Layout.VerticalAlignment.Stretch
        };
        modal.Children.Add
        (
            new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0))
            }
        );

        var frame = new Border
        {
            Margin              = new Thickness(16),
            Background           = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
            BorderBrush          = new SolidColorBrush(Color.FromRgb(92, 92, 92)),
            BorderThickness      = new Thickness(1),
            HorizontalAlignment  = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment    = Avalonia.Layout.VerticalAlignment.Center,
            Child                = content
        };

        if (window is SettingsWindow or ProjectEditWindow)
        {
            frame.Margin             = new Thickness(8);
            frame.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            frame.VerticalAlignment   = Avalonia.Layout.VerticalAlignment.Stretch;
        }
        else
        {
            if (!double.IsNaN(window.Width) && window.Width > 0)
                frame.MaxWidth = window.Width;

            if (!double.IsNaN(window.Height) && window.Height > 0)
                frame.MaxHeight = window.Height;
        }

        modal.Children.Add(frame);
        return modal;
    }

    private void AddWindow(Window window, Control modal)
    {
        if (overlay is null)
            throw new InvalidOperationException("远程窗口宿主尚未连接");

        if (window is IRemoteDialogOwner owner)
            owner.RemoteDialogHost = this;

        openWindows.Add(modal);
        overlay.Children.Add(modal);
    }

    private void RemoveWindow(Window window, Control modal, Control content)
    {
        overlay?.Children.Remove(modal);
        openWindows.Remove(modal);

        if (window is IRemoteDialogOwner owner)
            owner.RemoteDialogHost = null;

        if (window is SettingsWindow settingsWindow)
            settingsWindow.SetRemoteCloseAction(null);
        else if (window is ProjectEditWindow projectEditWindow)
            projectEditWindow.SetRemoteCloseAction(null);
        else if (window is PromptDialog promptDialog)
            promptDialog.SetRemoteCompletion(null);

        window.Content = content;
    }
}
