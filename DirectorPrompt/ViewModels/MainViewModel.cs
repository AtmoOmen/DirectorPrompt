using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Agents;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using DirectorPrompt.Localization;
using DirectorPrompt.Services;
using Serilog;

namespace DirectorPrompt.ViewModels;

public sealed partial class MainViewModel
(
    Orchestrator         orchestrator,
    IProjectRepository   projectRepository,
    ISessionRepository   sessionRepository,
    IEventRepository     eventRepository,
    IMemoryRepository    memoryRepository,
    DialogHistoryService dialogHistoryService,
    SidebarQueryService  sidebarQueryService,
    UserSettings         userSettings,
    IProjectPortService  projectPortService,
    NotificationService  notificationService,
    IUserSettingsStore   userSettingsStore,
    IWindowService       windowService,
    IFilePickerService   filePickerService,
    ILanSharingService   lanSharingService
)
    : ObservableObject
{
    private CancellationTokenSource? generationCts;
    private CancellationTokenSource? sessionLoadCts;
    private long?                    previousDialogRoundID;

    private DialogEntryViewModel? errorStreamingEntry;
    private DialogEntryViewModel? errorDirectorEntry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectSelected))]
    [NotifyPropertyChangedFor(nameof(CanInteractProject))]
    public partial Project? CurrentProject { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSessionSelected))]
    public partial Session? CurrentSession { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = Loc.Get("Status.Ready");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotProcessing))]
    [NotifyPropertyChangedFor(nameof(CanInteractProject))]
    [NotifyPropertyChangedFor(nameof(ShowPipelineStages))]
    public partial bool IsProcessing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPipelineStages))]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial bool IsSessionSidebarExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsLoadingDialog { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingEarlierDialog { get; set; }

    [ObservableProperty]
    public partial bool HasEarlierDialogEntries { get; set; }

    public bool IsProjectSelected => CurrentProject is not null;

    public bool IsNotProcessing => !IsProcessing;

    public bool CanInteractProject => IsProjectSelected && !IsProcessing;

    public bool IsSessionSelected => CurrentSession is not null;

    public bool ShowPipelineStages => IsProcessing || HasError;

    public ILanSharingService LanSharingService => lanSharingService;

    public DialogViewModel Dialog { get; } = new();

    public DirectiveInputViewModel DirectiveInput { get; } = new();

    public StatePanelViewModel StatePanel { get; } = new();

    public DirectivesPanelViewModel DirectivesPanel { get; } = new();

    public CharacterPanelViewModel CharacterPanel { get; } = new();

    public MemoryPanelViewModel MemoryPanel { get; } = new();

    public ObservableCollection<Project> Projects { get; } = [];

    public ObservableCollection<Session> Sessions { get; } = [];

    public ObservableCollection<PipelineStageViewModel> PipelineStages { get; } = [];

    [RelayCommand]
    private async Task LoadProjectsAsync()
    {
        try
        {
            Log.Information("加载项目列表");

            var projects = await projectRepository.GetAllAsync();

            var previousID = CurrentProject?.ID ?? userSettings.Session.LastProjectID;

            Projects.Clear();

            foreach (var project in projects)
                Projects.Add(project);

            if (previousID.HasValue)
                CurrentProject = Projects.FirstOrDefault(p => p.ID == previousID.Value);

            Log.Information("项目列表加载完成: 数量={Count}", Projects.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载项目列表失败");
            StatusMessage = Loc.Get("Status.LoadFailed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task LoadSessionsAsync()
    {
        if (CurrentProject is null)
            return;

        try
        {
            Log.Information("加载对话列表: 项目={ProjectID}", CurrentProject.ID);

            var sessions = await sessionRepository.GetByProjectAsync(CurrentProject.ID);

            var previousID = CurrentSession?.ID ?? userSettings.Session.LastSessionID;

            Sessions.Clear();

            foreach (var session in sessions)
                Sessions.Add(session);

            if (previousID.HasValue)
                CurrentSession = Sessions.FirstOrDefault(s => s.ID == previousID.Value);

            Log.Information("对话列表加载完成: 数量={Count}", Sessions.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载对话列表失败");
            StatusMessage = Loc.Get("Status.LoadFailed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task NewSessionAsync()
    {
        if (CurrentProject is null)
            return;

        try
        {
            var now = DateTime.UtcNow;

            var session = await sessionRepository.CreateAsync
                          (
                              new Session
                              {
                                  ProjectID = CurrentProject.ID,
                                  Title     = $"对话 {DateTime.Now:MM-dd HH:mm}",
                                  CreatedAt = now,
                                  UpdatedAt = now
                              }
                          );

            Log.Information("创建对话: ID={SessionID}, 项目={ProjectID}", session.ID, CurrentProject.ID);

            await LoadSessionsAsync();
            CurrentSession = Sessions.FirstOrDefault(s => s.ID == session.ID);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建对话失败");
            StatusMessage = Loc.Get("Status.CreateSessionFailed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        var name = await windowService.InputAsync
                   (
                       Loc.Get("Project.NewTitle"),
                       Loc.Get("Dialog.NewProjectPrompt"),
                       string.Empty
                   );

        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            var project = new Project { Name = name.Trim() };

            var created = await projectRepository.CreateAsync(project);

            Log.Information("创建项目: ID={ProjectID}, 名称={Name}", created.ID, created.Name);

            await LoadProjectsAsync();
            CurrentProject = Projects.FirstOrDefault(p => p.ID == created.ID);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建项目失败");
            StatusMessage = Loc.Get("Status.CreateProjectFailed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task EditProjectAsync()
    {
        if (CurrentProject is null)
            return;

        if (await windowService.EditProjectAsync(CurrentProject))
            await LoadProjectsAsync();
    }

    [RelayCommand]
    private async Task DeleteProjectAsync(Project project)
    {
        try
        {
            await projectRepository.DeleteAsync(project.ID);

            Log.Information("删除项目: ID={ProjectID}, 名称={Name}", project.ID, project.Name);

            if (CurrentProject?.ID == project.ID)
                CurrentProject = null;

            await LoadProjectsAsync();
            StatusMessage = Loc.Get("Status.ProjectDeleted");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除项目失败: ID={ProjectID}", project.ID);
            StatusMessage = Loc.Get("Status.DeleteProjectFailed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportProjectAsync()
    {
        if (CurrentProject is null)
        {
            StatusMessage = Loc.Get("Status.SelectProjectFirst");
            return;
        }

        var fileName = await filePickerService.SaveAsync
                       (
                           $"DirectorPrompt {Loc.Get("Project.Import.DirectorPrompt.Package")}",
                           "*.dppkg",
                           $"{CurrentProject.Name}.dppkg"
                       );

        if (fileName is null)
            return;

        IsProcessing  = true;
        StatusMessage = Loc.Get("Status.Exporting");

        try
        {
            await projectPortService.ExportAsync(CurrentProject.ID, fileName);

            Log.Information("导出项目: ID={ProjectID}, 路径={Path}", CurrentProject.ID, fileName);
            StatusMessage = Loc.Get("Status.ExportComplete");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "导出项目失败");
            StatusMessage = Loc.Get("Status.ExportFailed", ex.Message);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task ImportProjectAsync()
    {
        var fileName = await filePickerService.OpenAsync
                           ($"DirectorPrompt {Loc.Get("Project.Import.DirectorPrompt.Package")}", "*.dppkg");

        if (fileName is null)
            return;

        IsProcessing  = true;
        StatusMessage = Loc.Get("Status.Importing");

        try
        {
            var result = await projectPortService.ImportAsync(fileName);

            Log.Information
            (
                "导入项目: ID={ProjectID}, 名称={Name}, 知识={Knowledge}, 属性={State}",
                result.ProjectID,
                result.ProjectName,
                result.KnowledgeEntryCount,
                result.StateAttributeCount
            );

            await LoadProjectsAsync();
            CurrentProject = Projects.FirstOrDefault(p => p.ID == result.ProjectID);

            StatusMessage = Loc.Get("Status.ImportComplete", result.ProjectName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "导入项目失败");
            StatusMessage = Loc.Get("Status.ImportFailed", ex.Message);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task ImportSillyTavernProjectAsync()
    {
        var fileName = await filePickerService.OpenAsync
                           ($"SillyTavern {Loc.Get("Project.Import.SillyTavern.CharacterCard")}", "*.json");

        if (fileName is null)
            return;

        IsProcessing  = true;
        StatusMessage = Loc.Get("Status.Importing");

        try
        {
            var result = await projectPortService.ImportSillyTavernAsync(fileName);

            Log.Information
            (
                "导入 SillyTavern 角色卡: ID={ProjectID}, 名称={Name}, 知识={Knowledge}",
                result.ProjectID,
                result.ProjectName,
                result.KnowledgeEntryCount
            );

            await LoadProjectsAsync();
            CurrentProject = Projects.FirstOrDefault(p => p.ID == result.ProjectID);

            StatusMessage = Loc.Get("Status.ImportComplete", result.ProjectName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "导入 SillyTavern 角色卡失败");
            StatusMessage = Loc.Get("Status.ImportFailed", ex.Message);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSessionAsync(Session session)
    {
        try
        {
            await sessionRepository.DeleteAsync(session.ID);

            Log.Information("删除对话: ID={SessionID}, 标题={Title}", session.ID, session.Title);

            if (CurrentSession?.ID == session.ID)
                CurrentSession = null;

            await LoadSessionsAsync();
            StatusMessage = Loc.Get("Status.SessionDeleted");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除对话失败: ID={SessionID}", session.ID);
            StatusMessage = Loc.Get("Status.DeleteSessionFailed", ex.Message);
        }
    }

    public async Task RenameSessionAsync(Session session, string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle))
            return;

        try
        {
            var updated = session with { Title = newTitle.Trim() };

            await sessionRepository.UpdateAsync(updated);

            Log.Information("重命名对话: ID={SessionID}, 新标题={NewTitle}", session.ID, updated.Title);

            var existing = Sessions.FirstOrDefault(s => s.ID == session.ID);

            if (existing is not null)
            {
                var index = Sessions.IndexOf(existing);
                Sessions[index] = updated;
            }

            if (CurrentSession?.ID == session.ID)
                CurrentSession = updated;

            StatusMessage = Loc.Get("Status.SessionRenamed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "重命名对话失败: ID={SessionID}", session.ID);
            StatusMessage = Loc.Get("Status.RenameSessionFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void ToggleSessionSidebar() =>
        IsSessionSidebarExpanded = !IsSessionSidebarExpanded;

    [RelayCommand]
    private Task OpenSettingsAsync() =>
        windowService.ShowSettingsAsync();

    [RelayCommand]
    private void OpenLanSharing()
    {
        if (lanSharingService.Endpoint is null)
            return;

        Process.Start(new ProcessStartInfo(lanSharingService.Endpoint.AbsoluteUri) { UseShellExecute = true });
    }

    private void ResetPipelineStages() =>
        PipelineStages.Clear();

    private void UpdatePipelineStage(PipelineStageKind stage, PipelineStageStatus status, string? detail = null)
    {
        var existing = PipelineStages.FirstOrDefault(s => s.Kind == stage);

        if (existing is not null)
        {
            existing.Status = status;
            existing.Detail = detail;
        }
        else
            PipelineStages.Add(new PipelineStageViewModel { Kind = stage, Status = status, Detail = detail });
    }

    [RelayCommand]
    private async Task SendDirectivesAsync()
    {
        if (CurrentProject is null)
        {
            StatusMessage = Loc.Get("Status.SelectProjectFirst");
            return;
        }

        if (CurrentSession is null)
        {
            StatusMessage = Loc.Get("Status.SelectSessionFirst");
            return;
        }

        if (DirectiveInput.Directives.Count == 0)
        {
            StatusMessage = Loc.Get("Status.AddAtLeastOneDirective");
            return;
        }

        CancelGeneration();
        generationCts = new CancellationTokenSource();
        var token = generationCts.Token;

        var sessionID = CurrentSession.ID;

        IsProcessing  = true;
        StatusMessage = Loc.Get("Status.Processing");
        ResetPipelineStages();
        RemoveErrorEntries();
        ClearErrorState();

        DialogEntryViewModel? streamingEntry  = null;
        DialogEntryViewModel? directorEntry   = null;
        long                  expectedRoundID = 0;
        List<DirectiveItem>?  items           = null;

        var              streamingBuffer    = new StreamingBuffer();
        DispatcherTimer? streamingTimer     = null;
        EventHandler?    streamingTimerTick = null;

        try
        {
            items = DirectiveInput.Directives
                                  .Select(d => new DirectiveItem(d.Type, d.Content, d.Order, d.TTL))
                                  .ToList();

            var batch = new DirectiveBatch(CurrentProject.ID, items);

            Log.Information
            (
                "用户发送指令: 项目={ProjectID} ({ProjectName}), 对话={SessionID}, 指令数={Count}",
                CurrentProject.ID,
                CurrentProject.Name,
                sessionID,
                items.Count
            );

            directorEntry = Dialog.AddDirectorEntry(0, items.Select(d => (d.Type, d.Content)).ToList());

            DirectiveInput.Clear();

            streamingEntry = Dialog.BeginStreamingNarrative(0);

            var dispatcher = Dispatcher.UIThread;
            streamingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            streamingTimerTick  =  (_, _) => FlushStreamingUpdate();
            streamingTimer.Tick += streamingTimerTick;
            streamingTimer.Start();

            expectedRoundID = await eventRepository.GetLatestRoundIDAsync(sessionID, token) + 1;

            var result = await orchestrator.ProcessBatchAsync(batch, sessionID, StreamingUpdate, StageUpdate, token);

            StopStreamingTimer();
            FlushStreamingUpdate();

            if (CurrentSession?.ID != sessionID)
            {
                await orchestrator.DeleteRoundAsync(sessionID, result.RoundID, token);
                return;
            }

            directorEntry.RoundID   = result.RoundID;
            streamingEntry.RoundID  = result.RoundID;
            streamingEntry.Content  = result.Narrative;
            streamingEntry.Thinking = result.Thinking;
            streamingEntry.RenderMarkdown();

            Log.Information
            (
                "指令处理完成: 轮次={RoundID}, 叙事长度={NarrativeLen}",
                result.RoundID,
                result.Narrative.Length
            );

            await RefreshSidebarAsync(token);

            StatusMessage = Loc.Get("Status.Complete");
            notificationService.Notify
            (
                Loc.Get("Notification.TaskComplete.Title"),
                Loc.Get("Notification.TaskComplete.Message")
            );

            void StreamingUpdate(string narrativeDelta, string thinkingDelta, bool isFullSnapshot) =>
                streamingBuffer.Append(narrativeDelta, thinkingDelta, isFullSnapshot);

            void FlushStreamingUpdate()
            {
                if (!streamingBuffer.TryGetSnapshot(out var narrative, out var thinking, out var isFullSnapshot))
                    return;

                if (CurrentSession?.ID == sessionID && streamingEntry is not null)
                    streamingEntry.UpdateStreamingContent(narrative, thinking, isFullSnapshot);
            }

            void StopStreamingTimer()
            {
                if (streamingTimer is null)
                    return;

                if (streamingTimerTick is not null)
                    streamingTimer.Tick -= streamingTimerTick;

                streamingTimer.Stop();
                streamingTimer = null;
            }

            void StageUpdate(PipelineStageUpdate update) =>
                dispatcher.Post
                (() =>
                    {
                        if (CurrentSession?.ID == sessionID)
                            UpdatePipelineStage(update.Stage, update.Status, update.Detail);
                    }
                );
        }
        catch (OperationCanceledException)
        {
            await RollbackRoundAsync(sessionID, expectedRoundID, streamingEntry, directorEntry);
            StatusMessage = Loc.Get("Status.Cancelled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "处理指令失败");

            await RollbackRoundAsync(sessionID, expectedRoundID);

            errorStreamingEntry = streamingEntry;
            errorDirectorEntry  = directorEntry;
            HasError            = true;

            if (CurrentSession?.ID == sessionID)
            {
                DirectiveInput.Clear();

                if (items is not null)
                {
                    foreach (var item in items)
                    {
                        DirectiveInput.Directives.Add
                        (
                            new DirectiveItemViewModel
                            {
                                Type    = item.Type,
                                Content = item.Content,
                                Order   = item.Order,
                                TTL     = item.TTL
                            }
                        );
                    }
                }

                if (streamingEntry is not null)
                {
                    await Dispatcher.UIThread.InvokeAsync
                    (() => streamingEntry.SetError(ex.Message)
                    );
                }
            }

            StatusMessage = Loc.Get("Status.ProcessFailed", ex.Message);
        }
        finally
        {
            if (streamingTimer is not null)
            {
                if (streamingTimerTick is not null)
                    streamingTimer.Tick -= streamingTimerTick;

                streamingTimer.Stop();
            }

            if (CurrentSession?.ID == sessionID)
                IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task RollbackLastRoundAsync()
    {
        if (CurrentSession is null)
            return;

        IsProcessing  = true;
        StatusMessage = Loc.Get("Status.RollingBack");

        try
        {
            var result = await orchestrator.RollbackLastRoundAsync(CurrentSession.ID);

            if (result is null)
            {
                StatusMessage = Loc.Get("Status.NoRoundToRollback");
                return;
            }

            Dialog.RemoveEntriesByRound(result.RoundID);

            await RefreshSidebarAsync();

            if (result.Directives.Count > 0)
            {
                DirectiveInput.Clear();

                foreach (var d in result.Directives)
                    DirectiveInput.Directives.Add(new DirectiveItemViewModel { Type = d.Type, Content = d.Content, Order = d.Order, TTL = d.TTL });
            }

            StatusMessage = Loc.Get("Status.RolledBack");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "回退失败");
            StatusMessage = Loc.Get("Status.RollbackFailed", ex.Message);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task SaveEditAsync(DialogEntryViewModel entry)
    {
        entry.CommitEdit();

        if (entry.EventID.HasValue)
        {
            try
            {
                await eventRepository.UpdateEventDataAsync(entry.EventID.Value, entry.Content);

                Log.Information("手动编辑已保存: 事件ID={EventID}", entry.EventID.Value);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "保存手动编辑失败: 事件ID={EventID}", entry.EventID.Value);
                StatusMessage = Loc.Get("Status.SaveEditFailed");
            }
        }
    }

    [RelayCommand]
    private void CancelEdit(DialogEntryViewModel entry) =>
        entry.CancelEdit();

    private void RemoveErrorEntries()
    {
        if (errorStreamingEntry is not null)
            Dialog.Entries.Remove(errorStreamingEntry);

        if (errorDirectorEntry is not null)
            Dialog.Entries.Remove(errorDirectorEntry);
    }

    private void ClearErrorState()
    {
        errorStreamingEntry = null;
        errorDirectorEntry  = null;
        HasError            = false;
    }

    partial void OnCurrentProjectChanged(Project? value)
    {
        CancelGeneration();
        sessionLoadCts?.Cancel();

        CurrentSession = null;
        Sessions.Clear();
        Dialog.Clear();
        ClearErrorState();

        if (value is not null)
        {
            Log.Information("切换项目: ID={ProjectID}, 名称={Name}", value.ID, value.Name);
            _ = LoadSessionsAsync();
        }
    }

    partial void OnCurrentSessionChanged(Session? value)
    {
        CancelGeneration();

        sessionLoadCts?.Cancel();
        sessionLoadCts = new CancellationTokenSource();
        var token = sessionLoadCts.Token;

        IsLoadingDialog = false;
        Dialog.Clear();
        previousDialogRoundID = null;
        HasEarlierDialogEntries = false;
        DirectiveInput.Clear();
        ResetPipelineStages();
        ClearErrorState();
        _ = SaveSessionStateAsync();

        if (value is null)
        {
            StatePanel.Clear();
            DirectivesPanel.Clear();
            CharacterPanel.Clear();
            MemoryPanel.Clear();
            return;
        }

        Log.Information("切换对话: ID={SessionID}", value.ID);

        if (CurrentProject is not null && !string.IsNullOrWhiteSpace(CurrentProject.OpeningMessage))
            Dialog.AddOpeningMessage(CurrentProject.OpeningMessage, true);

        _ = LoadSessionDataAsync(value.ID, token);
    }

    private void CancelGeneration() =>
        generationCts?.Cancel();

    private async Task LoadSessionDataAsync(long sessionID, CancellationToken token)
    {
        try
        {
            await LoadDialogHistoryAsync(sessionID, token);

            if (token.IsCancellationRequested)
                return;

            await RefreshSidebarAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RollbackRoundAsync
    (
        long                  sessionID,
        long                  expectedRoundID,
        DialogEntryViewModel? streamingEntry = null,
        DialogEntryViewModel? directorEntry  = null
    )
    {
        try
        {
            await orchestrator.TryDeleteRoundAsync(sessionID, expectedRoundID);

            if (CurrentSession?.ID == sessionID)
            {
                if (streamingEntry is not null)
                    Dialog.Entries.Remove(streamingEntry);

                if (directorEntry is not null)
                    Dialog.Entries.Remove(directorEntry);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "回退失败: 对话={SessionID}, 轮次={RoundID}", sessionID, expectedRoundID);
        }
    }

    private async Task SaveSessionStateAsync()
    {
        try
        {
            userSettings.Session.LastProjectID = CurrentProject?.ID;
            userSettings.Session.LastSessionID = CurrentSession?.ID;

            await userSettingsStore.SaveAsync(userSettings);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "保存会话状态失败");
        }
    }

    private async Task LoadDialogHistoryAsync(long sessionID, CancellationToken token = default)
    {
        IsLoadingDialog = true;

        try
        {
            var result = await dialogHistoryService.LoadAsync(sessionID, token: token);

            if (token.IsCancellationRequested)
                return;

            AddDialogHistory(result, false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载对话历史失败: 对话={SessionID}", sessionID);
        }
        finally
        {
            if (!token.IsCancellationRequested && CurrentSession?.ID == sessionID)
                IsLoadingDialog = false;
        }
    }

    public async Task LoadEarlierDialogHistoryAsync()
    {
        if (CurrentSession is null || previousDialogRoundID is null || IsLoadingEarlierDialog)
            return;

        var sessionID = CurrentSession.ID;
        var token     = sessionLoadCts?.Token ?? CancellationToken.None;
        IsLoadingEarlierDialog = true;

        try
        {
            var result = await dialogHistoryService.LoadAsync(sessionID, previousDialogRoundID, token);

            if (token.IsCancellationRequested || CurrentSession?.ID != sessionID)
                return;

            AddDialogHistory(result, true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载更早对话历史失败: 对话={SessionID}", sessionID);
        }
        finally
        {
            IsLoadingEarlierDialog = false;
        }
    }

    private void AddDialogHistory(DialogHistoryResult result, bool prepend)
    {
        var entries = new List<DialogEntryViewModel>();

        foreach (var round in result.Rounds)
        {
            if (round.DirectorBlocks.Count > 0)
            {
                var entry = new DialogEntryViewModel
                {
                    RoundID = round.RoundID,
                    EventID = round.DirectorEventID,
                    Type    = EventType.DirectorInput,
                    Content = string.Join("\n", round.DirectorBlocks.Select(block => $"[{block.Type}] {block.Content}"))
                };

                foreach (var block in round.DirectorBlocks)
                {
                    entry.DirectorBlocks.Add
                    (
                        new DirectorContentBlockViewModel
                        {
                            Type    = block.Type,
                            Content = block.Content
                        }
                    );
                }

                entries.Add(entry);
            }

            if (!string.IsNullOrWhiteSpace(round.NarrativeText))
            {
                entries.Add
                (
                    new DialogEntryViewModel
                    {
                        RoundID = round.RoundID,
                        EventID = round.NarrativeEventID,
                        Type    = EventType.NarrativeOutput,
                        Content = round.NarrativeText
                    }
                );
            }
        }

        if (prepend)
        {
            for (var index = entries.Count - 1; index >= 0; index--)
                Dialog.Entries.Insert(0, entries[index]);
        }
        else
        {
            if (Dialog.Entries.Count > 0 && entries.Count > 0)
                Dialog.Entries[^1].IsLast = false;

            foreach (var entry in entries)
                Dialog.Entries.Add(entry);

            if (entries.Count > 0)
                entries[^1].IsLast = true;
        }

        previousDialogRoundID   = result.PreviousRoundID;
        HasEarlierDialogEntries = previousDialogRoundID is not null;
    }

    private async Task RefreshSidebarAsync(CancellationToken token = default)
    {
        if (CurrentSession is null || CurrentProject is null)
            return;

        var sessionID = CurrentSession.ID;

        await RefreshStatePanelAsync();

        if (token.IsCancellationRequested || CurrentSession?.ID != sessionID)
            return;

        await RefreshDirectivesPanelAsync();

        if (token.IsCancellationRequested || CurrentSession?.ID != sessionID)
            return;

        await RefreshCharacterPanelAsync();

        if (token.IsCancellationRequested || CurrentSession?.ID != sessionID)
            return;

        await RefreshMemoryPanelAsync();
    }

    private async Task RefreshStatePanelAsync()
    {
        if (CurrentSession is null || CurrentProject is null)
            return;

        StatePanel.Clear();

        var data = await sidebarQueryService.QueryStatePanelAsync(CurrentProject.ID, CurrentSession.ID);

        StatePanel.CurrentSceneLabel = data.SceneLabel;

        foreach (var item in data.Items)
        {
            StatePanel.StateItems.Add
            (
                new StateItemViewModel
                {
                    Name  = item.Name,
                    Value = item.Value,
                    Scope = item.Scope
                }
            );
        }
    }

    private async Task RefreshDirectivesPanelAsync()
    {
        if (CurrentSession is null)
            return;

        DirectivesPanel.Clear();

        var data = await sidebarQueryService.QueryDirectivesPanelAsync(CurrentSession.ID);

        foreach (var d in data.Items)
        {
            DirectivesPanel.Directives.Add
            (
                new DirectivePanelItemViewModel
                {
                    Type = d.Type switch
                    {
                        DirectiveType.Tone                => "🎭",
                        DirectiveType.TemporaryConstraint => "🚫",
                        DirectiveType.SceneChange         => "🎬",
                        _                                 => "📝"
                    },
                    Content = d.Content,
                    HasTTL  = d.HasTTL,
                    TTLLabel = d.HasTTL && d.TTL.HasValue ?
                                   Loc.Get("Directive.Panel.RemainingRounds", d.TTL) :
                                   Loc.Get("Directive.Panel.Permanent")
                }
            );
        }
    }

    private async Task RefreshCharacterPanelAsync()
    {
        if (CurrentSession is null || CurrentProject is null)
            return;

        var data = await sidebarQueryService.QueryCharacterPanelAsync(CurrentProject.ID, CurrentSession.ID);

        var localGroups = new List<CharacterCategoryGroupViewModel>();

        foreach (var grp in data.Groups)
        {
            var groupName = grp.CategoryName ?? Loc.Get("Character.Panel.Uncategorized");

            var group = new CharacterCategoryGroupViewModel
            {
                CategoryName = groupName
            };

            foreach (var item in grp.Items)
            {
                var vm = new CharacterPanelItemViewModel
                {
                    ID          = item.ID,
                    Name        = item.Name,
                    Description = item.Description,
                    Categories  = item.Categories
                };

                foreach (var sv in item.StateValues)
                    vm.StateValues.Add(new CharacterStateValueViewModel { Name = sv.Name, Value = sv.Value });

                foreach (var r in item.Relations)
                    vm.Relations.Add(new CharacterRelationViewModel { Target = r.Target, Type = r.Type, Description = r.Description, Direction = r.Direction });

                group.Items.Add(vm);
            }

            localGroups.Add(group);
        }

        CharacterPanel.SetGroups(localGroups);
    }

    private async Task RefreshMemoryPanelAsync()
    {
        if (CurrentSession is null || CurrentProject is null)
            return;

        var data = await sidebarQueryService.QueryMemoryPanelAsync(CurrentSession.ID);

        var localGroups = new List<MemorySceneGroupViewModel>();

        foreach (var grp in data.Groups)
        {
            var group = new MemorySceneGroupViewModel
            {
                SceneLabel = grp.SceneLabel
            };

            foreach (var m in grp.Items)
            {
                group.Items.Add
                (
                    new MemoryPanelItemViewModel
                    {
                        ID                   = m.ID,
                        Content              = m.Content,
                        TagsDisplay          = m.TagsDisplay,
                        SceneLabel           = m.SceneLabel,
                        TimelinePos          = m.TimelinePos,
                        RelatedCharacters    = m.RelatedCharacters,
                        HasRelatedCharacters = m.HasRelatedCharacters,
                        UpdatedAtDisplay     = m.UpdatedAtDisplay
                    }
                );
            }

            localGroups.Add(group);
        }

        MemoryPanel.SetGroups(localGroups);
    }

    [RelayCommand]
    private async Task SaveMemoryEditAsync(MemoryPanelItemViewModel item)
    {
        item.CommitEdit();

        try
        {
            var existing = await memoryRepository.GetByIDAsync(item.ID);

            if (existing is null)
                return;

            var tags = item.TagsDisplay
                           .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var updated = existing with { Content = item.Content, Tags = tags };

            await memoryRepository.UpdateAsync(updated, CurrentSession?.ID ?? 0, 0);

            Log.Information("记忆编辑已保存: ID={MemoryID}", item.ID);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存记忆编辑失败: ID={MemoryID}", item.ID);
            StatusMessage = Loc.Get("Status.SaveMemoryFailed");
        }
    }

    [RelayCommand]
    private void CancelMemoryEdit(MemoryPanelItemViewModel item) =>
        item.CancelEdit();

    [RelayCommand]
    private async Task DeleteMemoryAsync(MemoryPanelItemViewModel item)
    {
        try
        {
            await memoryRepository.DeleteAsync(item.ID, CurrentSession?.ID ?? 0, 0);

            MemoryPanel.RemoveItem(item);

            Log.Information("记忆已删除: ID={MemoryID}", item.ID);
            StatusMessage = Loc.Get("Status.MemoryDeleted");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除记忆失败: ID={MemoryID}", item.ID);
            StatusMessage = Loc.Get("Status.DeleteMemoryFailed", ex.Message);
        }
    }

    private sealed class StreamingBuffer
    {
        private readonly Lock          streamingLock = new();
        private readonly StringBuilder narrative     = new();
        private readonly StringBuilder thinking      = new();

        private bool hasUpdate;
        private bool hasFullSnapshot;

        public void Append(string narrativeDelta, string thinkingDelta, bool isFullSnapshot)
        {
            lock (streamingLock)
            {
                if (isFullSnapshot)
                {
                    narrative.Clear();
                    thinking.Clear();
                    hasFullSnapshot = true;
                }

                if (!string.IsNullOrEmpty(narrativeDelta))
                    narrative.Append(narrativeDelta);

                if (!string.IsNullOrEmpty(thinkingDelta))
                    thinking.Append(thinkingDelta);

                hasUpdate = true;
            }
        }

        public bool TryGetSnapshot(out string narrativeText, out string thinkingText, out bool isFullSnapshot)
        {
            lock (streamingLock)
            {
                if (!hasUpdate)
                {
                    narrativeText  = string.Empty;
                    thinkingText   = string.Empty;
                    isFullSnapshot = false;
                    return false;
                }

                narrativeText   = narrative.ToString();
                thinkingText    = thinking.ToString();
                isFullSnapshot  = hasFullSnapshot;
                hasUpdate       = false;
                hasFullSnapshot = false;
                return true;
            }
        }
    }
}

public sealed class PipelineStageViewModel : INotifyPropertyChanged
{
    private PipelineStageStatus status;
    private string?             detail;

    public PipelineStageKind Kind { get; init; }

    public string Stage => Loc.Get
    (
        Kind switch
        {
            PipelineStageKind.DirectiveProcessing => "Pipeline.Stage.DirectiveProcessing",
            PipelineStageKind.Retrieval           => "Pipeline.Stage.Retrieval",
            PipelineStageKind.Generation          => "Pipeline.Stage.Generation",
            PipelineStageKind.PostProcessing      => "Pipeline.Stage.PostProcessing",
            _                                     => Kind.ToString()
        }
    );

    public PipelineStageStatus Status
    {
        get => status;
        set
        {
            if (status != value)
            {
                status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            }
        }
    }

    public string StatusText => Loc.Get
    (
        status switch
        {
            PipelineStageStatus.Running  => "Pipeline.Status.Running",
            PipelineStageStatus.Complete => "Pipeline.Status.Complete",
            PipelineStageStatus.Failed   => "Pipeline.Status.Failed",
            _                            => status.ToString()
        }
    );

    public string? Detail
    {
        get => detail;
        set
        {
            if (detail != value)
            {
                detail = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
