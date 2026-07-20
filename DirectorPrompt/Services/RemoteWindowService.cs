using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Localization;
using DirectorPrompt.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DirectorPrompt.Services;

public sealed class RemoteWindowService
(
    IServiceProvider               serviceProvider,
    UserSettings                   userSettings,
    ILanSharingService             lanSharingService,
    IProjectEditWindowCoordinator? projectEditWindowCoordinator = null
) : IWindowService, IRemoteDialogHost
{
    private readonly List<Control> openWindows = [];

    private Panel?  overlay;
    private Canvas? popupLayer;

    public void Attach(Panel overlay, Canvas popupLayer)
    {
        this.overlay      = overlay;
        this.popupLayer   = popupLayer;
        overlay.IsVisible = true;
        RemotePopupHost.Attach(popupLayer);
        Log.Information("远程窗口宿主已连接");
    }

    public void Detach()
    {
        Log.Information("开始断开远程窗口宿主: 打开窗口数={OpenWindowCount}", openWindows.Count);

        if (popupLayer is not null)
            RemotePopupHost.Detach(popupLayer);

        if (overlay is not null)
            overlay.Children.Clear();

        openWindows.Clear();
        overlay    = null;
        popupLayer = null;
        Log.Information("远程窗口宿主已断开");
    }

    public Task<string?> InputAsync(string title, string prompt, string defaultValue) =>
        ShowInputAsync(title, prompt, defaultValue, false);

    public async Task<bool> EditProjectAsync(Project project)
    {
        Log.Information("通过远程界面打开项目编辑窗口: 项目={ProjectID}", project.ID);

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
        Log.Information("通过远程界面打开设置窗口");

        var window = serviceProvider.GetRequiredService<SettingsWindow>();
        window.RemoteDialogHost = this;

        var saved = await ShowWindowAsync<bool>
                    (
                        window,
                        completion => window.SetRemoteCloseAction(completion)
                    );

        if (!saved)
        {
            Log.Information("远程设置窗口未保存即关闭");
            return;
        }

        try
        {
            await lanSharingService.ApplyAsync(userSettings.RemoteControl.IsLanSharingEnabled);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "远程设置应用局域网共享失败");
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
        bool   danger
    )
    {
        Log.Debug("显示远程确认对话框: 危险操作={Danger}", danger);

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
        bool   multiline
    )
    {
        Log.Debug("显示远程输入对话框: 多行={Multiline}, 默认值长度={DefaultValueLength}", multiline, defaultValue.Length);

        var dialog     = new PromptDialog();
        var completion = dialog.ShowRemoteInputAsync(title, prompt, defaultValue, multiline);

        return ShowPromptAsync(dialog, completion);
    }

    private async Task ShowErrorAsync(string title, string message) =>
        await ShowConfirmationAsync
        (
            title,
            message,
            Loc.Get("Common.Close"),
            string.Empty,
            false
        );

    private async Task<TResult> ShowWindowAsync<TResult>
    (
        Window                  window,
        Action<Action<TResult>> setCompletion
    )
    {
        Log.Information("显示远程窗口: 类型={WindowType}", window.GetType().Name);

        if (window is SettingsWindow settingsWindow)
            settingsWindow.UseRemoteLayout();
        else if (window is ProjectEditWindow projectEditWindow)
            projectEditWindow.UseRemoteLayout();

        var content    = DetachContent(window);
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
            Log.Information("远程窗口已关闭: 类型={WindowType}", window.GetType().Name);
        }
    }

    private async Task<TResult> ShowPromptAsync<TResult>
    (
        PromptDialog  dialog,
        Task<TResult> completion
    )
    {
        Log.Information("显示远程提示对话框: 类型={DialogType}", dialog.GetType().Name);

        var content = DetachContent(dialog);
        var modal   = CreateModal(dialog, content);
        AddWindow(dialog, modal);

        try
        {
            return await completion;
        }
        finally
        {
            RemoveWindow(dialog, modal, content);
            Log.Information("远程提示对话框已关闭: 类型={DialogType}", dialog.GetType().Name);
        }
    }

    private Control DetachContent(Window window)
    {
        if (window.Content is not Control content)
            throw new InvalidOperationException($"{window.GetType().Name} 内容无法用于远程显示");

        window.Content      = null;
        content.DataContext = window.DataContext;
        return content;
    }

    private Control CreateModal(Window window, Control content)
    {
        var modal = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch
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
            Background          = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
            BorderBrush         = new SolidColorBrush(Color.FromRgb(92, 92, 92)),
            BorderThickness     = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Child               = content
        };

        if (window is SettingsWindow or ProjectEditWindow)
        {
            frame.Margin              = new Thickness(8);
            frame.HorizontalAlignment = HorizontalAlignment.Stretch;
            frame.VerticalAlignment   = VerticalAlignment.Stretch;
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
        Log.Debug("远程窗口已加入覆盖层: 类型={WindowType}, 打开窗口数={OpenWindowCount}", window.GetType().Name, openWindows.Count);
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
        Log.Debug("远程窗口已从覆盖层移除: 类型={WindowType}, 打开窗口数={OpenWindowCount}", window.GetType().Name, openWindows.Count);
    }
}
