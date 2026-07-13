using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Agents;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using DirectorPrompt.Infrastructure.Extensions;
using DirectorPrompt.Localization;
using DirectorPrompt.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Serilog;

namespace DirectorPrompt.ViewModels;

public sealed partial class MainViewModel
(
    Orchestrator         orchestrator,
    IProjectRepository   projectRepository,
    ISessionRepository   sessionRepository,
    IEventRepository     eventRepository,
    ISceneRepository     sceneRepository,
    IStateRepository     stateRepository,
    ICharacterRepository characterRepository,
    IDirectiveRepository directiveRepository,
    IMemoryRepository    memoryRepository,
    IServiceProvider     serviceProvider,
    UserSettings         userSettings,
    IProjectPortService  projectPortService
)
    : ObservableObject
{
    private CancellationTokenSource? generationCts;
    private CancellationTokenSource? sessionLoadCts;

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

    public bool IsProjectSelected => CurrentProject is not null;

    public bool IsNotProcessing => !IsProcessing;

    public bool CanInteractProject => IsProjectSelected && !IsProcessing;

    public bool IsSessionSelected => CurrentSession is not null;

    public bool ShowPipelineStages => IsProcessing || HasError;

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
        var name = PromptDialog.Input
        (
            GetCurrentWindow(),
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

        var window = serviceProvider.GetRequiredService<ProjectEditWindow>();
        await window.ViewModel.LoadFromProjectAsync(CurrentProject);
        window.Owner = GetCurrentWindow();

        if (window.ShowDialog() == true)
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

        var dialog = new SaveFileDialog
        {
            Filter   = $"DirectorPrompt {Loc.Get("Project.Import.DirectorPrompt.Package")}|*.dppkg",
            FileName = $"{CurrentProject.Name}.dppkg"
        };

        if (dialog.ShowDialog() != true)
            return;

        IsProcessing  = true;
        StatusMessage = Loc.Get("Status.Exporting");

        try
        {
            await projectPortService.ExportAsync(CurrentProject.ID, dialog.FileName);

            Log.Information("导出项目: ID={ProjectID}, 路径={Path}", CurrentProject.ID, dialog.FileName);
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
        var dialog = new OpenFileDialog
        {
            Filter = $"DirectorPrompt {Loc.Get("Project.Import.DirectorPrompt.Package")}|*.dppkg"
        };

        if (dialog.ShowDialog() != true)
            return;

        IsProcessing  = true;
        StatusMessage = Loc.Get("Status.Importing");

        try
        {
            var result = await projectPortService.ImportAsync(dialog.FileName);

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
        var dialog = new OpenFileDialog
        {
            Filter = $"SillyTavern {Loc.Get("Project.Import.SillyTavern.CharacterCard")}|*.json"
        };

        if (dialog.ShowDialog() != true)
            return;

        IsProcessing  = true;
        StatusMessage = Loc.Get("Status.Importing");

        try
        {
            var result = await projectPortService.ImportSillyTavernAsync(dialog.FileName);

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
    private void OpenSettings()
    {
        var window = serviceProvider.GetRequiredService<SettingsWindow>();
        window.Owner = GetCurrentWindow();
        window.ShowDialog();
    }

    private static Window? GetCurrentWindow() => Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive);

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

        var streamingNarrativeSB = new StringBuilder();
        var streamingThinkingSB  = new StringBuilder();
        var streamingLock        = new object();
        var hasStreamingUpdate   = false;
        var hasFullSnapshot      = false;
        DispatcherTimer? streamingTimer = null;
        EventHandler? streamingTimerTick = null;

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

            var dispatcher = Application.Current.Dispatcher;
            streamingTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            streamingTimerTick = (_, _) => FlushStreamingUpdate();
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

            void StreamingUpdate(string narrativeDelta, string thinkingDelta, bool isFullSnapshot)
            {
                lock (streamingLock)
                {
                    if (isFullSnapshot)
                    {
                        streamingNarrativeSB.Clear();
                        streamingThinkingSB.Clear();
                        hasFullSnapshot = true;
                    }

                    if (!string.IsNullOrEmpty(narrativeDelta))
                        streamingNarrativeSB.Append(narrativeDelta);

                    if (!string.IsNullOrEmpty(thinkingDelta))
                        streamingThinkingSB.Append(thinkingDelta);

                    hasStreamingUpdate = true;
                }
            }

            void FlushStreamingUpdate()
            {
                string narrative;
                string thinking;
                bool isFullSnapshot;

                lock (streamingLock)
                {
                    if (!hasStreamingUpdate)
                        return;

                    narrative          = streamingNarrativeSB.ToString();
                    thinking           = streamingThinkingSB.ToString();
                    isFullSnapshot     = hasFullSnapshot;
                    hasStreamingUpdate = false;
                    hasFullSnapshot    = false;
                }

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
                dispatcher.BeginInvoke
                (
                    () =>
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
                    await Application.Current.Dispatcher.BeginInvoke
                    (
                        () => streamingEntry.SetError(ex.Message)
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
            var latestRound = await eventRepository.GetLatestRoundIDAsync(CurrentSession.ID);

            if (latestRound <= 0)
            {
                StatusMessage = Loc.Get("Status.NoRoundToRollback");
                return;
            }

            var events        = await eventRepository.GetByRoundAsync(CurrentSession.ID, latestRound);
            var directorEvent = events.FirstOrDefault(e => e.Type == EventType.DirectorInput);

            Log.Information("用户回退轮次: 对话={SessionID}, 轮次={RoundID}", CurrentSession.ID, latestRound);

            await orchestrator.DeleteRoundAsync(CurrentSession.ID, latestRound);

            Dialog.RemoveEntriesByRound(latestRound);

            await RefreshSidebarAsync();

            if (directorEvent is not null)
            {
                DirectiveInput.Clear();

                var directives = ParseDirectivesFromEvent(directorEvent.Data);

                foreach (var d in directives)
                    DirectiveInput.Directives.Add(new DirectiveItemViewModel { Type = d.Type, Content = d.Content, Order = d.Order });
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

        Dialog.Clear();
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
            Dialog.AddOpeningMessage(CurrentProject.OpeningMessage);

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
            if (expectedRoundID > 0)
            {
                var events = await eventRepository.GetByRoundAsync(sessionID, expectedRoundID);

                if (events.Count > 0)
                    await orchestrator.DeleteRoundAsync(sessionID, expectedRoundID);
            }

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

            await userSettings.SaveAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "保存会话状态失败");
        }
    }

    private async Task LoadDialogHistoryAsync(long sessionID, CancellationToken token = default)
    {
        try
        {
            var events = await eventRepository.GetBySessionAsync(sessionID, token);

            if (token.IsCancellationRequested)
                return;

            var directorEvents = events
                                 .Where(e => e.Type == EventType.DirectorInput)
                                 .GroupBy(e => e.RoundID)
                                 .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.ID).First());

            var narrativeEvents = events
                                  .Where(e => e.Type == EventType.NarrativeOutput)
                                  .GroupBy(e => e.RoundID)
                                  .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.ID).First());

            var roundIDs = directorEvents.Keys
                                         .Concat(narrativeEvents.Keys)
                                         .Distinct()
                                         .OrderBy(r => r)
                                         .ToList();

            foreach (var roundID in roundIDs)
            {
                if (directorEvents.TryGetValue(roundID, out var directorEvent))
                {
                    var directorBlocks = ParseDirectorInputBlocks(directorEvent.Data);
                    Dialog.AddDirectorEntry(roundID, directorBlocks);
                    Dialog.Entries[^1].EventID = directorEvent.ID;
                }

                if (narrativeEvents.TryGetValue(roundID, out var narrativeEvent))
                {
                    var narrativeText = narrativeEvent.Data;

                    if (!string.IsNullOrWhiteSpace(narrativeText))
                    {
                        Dialog.AddNarrativeEntry(roundID, narrativeText);
                        Dialog.Entries[^1].EventID = narrativeEvent.ID;
                    }
                }
            }

            Log.Information
            (
                "对话历史加载完成: 对话={SessionID}, 轮次数={RoundCount}",
                sessionID,
                roundIDs.Count
            );
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载对话历史失败: 对话={SessionID}", sessionID);
        }
    }

    private static IReadOnlyList<DirectiveItem> ParseDirectivesFromEvent(string jsonData)
    {
        var result = new List<DirectiveItem>();

        using var doc = JsonDocument.Parse(jsonData);

        var order = 1;

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var isSystem = element.TryGetProperty("isSystem", out var sysEl) && sysEl.GetBoolean();

            if (isSystem)
                continue;

            var typeStr = element.GetProperty("type").GetString()    ?? "Plot";
            var content = element.GetProperty("content").GetString() ?? string.Empty;

            var type = typeStr switch
            {
                "Tone"                => DirectiveType.Tone,
                "TemporaryConstraint" => DirectiveType.TemporaryConstraint,
                "SceneChange"         => DirectiveType.SceneChange,
                _                     => DirectiveType.Plot
            };

            result.Add(new DirectiveItem(type, content, order++));
        }

        return result;
    }

    private static List<(DirectiveType Type, string Content)> ParseDirectorInputBlocks(string json)
    {
        var result = new List<(DirectiveType Type, string Content)>();

        try
        {
            using var doc = JsonDocument.Parse(json);

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var isSystem = element.TryGetProperty("isSystem", out var sysEl) && sysEl.GetBoolean();

                if (isSystem)
                    continue;

                var typeStr = element.GetProperty("type").GetString()    ?? "Plot";
                var content = element.GetProperty("content").GetString() ?? string.Empty;

                var type = typeStr switch
                {
                    "Tone"                => DirectiveType.Tone,
                    "TemporaryConstraint" => DirectiveType.TemporaryConstraint,
                    "SceneChange"         => DirectiveType.SceneChange,
                    _                     => DirectiveType.Plot
                };

                result.Add((type, content));
            }
        }
        catch
        {
            return [(DirectiveType.Plot, json)];
        }

        return result;
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

        var scene = await sceneRepository.GetActiveSceneAsync(CurrentSession.ID);

        if (scene is not null)
            StatePanel.CurrentSceneLabel = scene.TimeLabel;

        var values     = await stateRepository.GetAllStateValuesAsync(CurrentProject.ID, CurrentSession.ID);
        var attributes = await stateRepository.GetAttributesAsync(CurrentProject.ID);

        foreach (var attr in attributes.Where(a => a.Scope == StateScope.Global))
        {
            var value = values.FirstOrDefault(v => v.AttributeID == attr.ID);

            StatePanel.StateItems.Add
            (
                new StateItemViewModel
                {
                    Name  = attr.DisplayName,
                    Value = value?.Value ?? "—",
                    Scope = attr.Scope.ToString()
                }
            );
        }
    }

    private async Task RefreshDirectivesPanelAsync()
    {
        if (CurrentSession is null)
            return;

        DirectivesPanel.Clear();

        var directives = await directiveRepository.GetActiveAsync(CurrentSession.ID);

        foreach (var d in directives)
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
                    HasTTL  = d.TTL.HasValue,
                    TTLLabel = d.TTL.HasValue ?
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

        var characters    = await characterRepository.GetBySessionAsync(CurrentSession.ID);
        var categories    = await characterRepository.GetCategoriesAsync(CurrentProject.ID);
        var categoryAttrs = await stateRepository.GetAttributesAsync(CurrentProject.ID, StateScope.Category);
        var charLookup    = characters.ToDictionary(c => c.ID);

        var categoryLookup = categories.ToDictionary(c => c.ID);

        var items = new List<(CharacterPanelItemViewModel Item, long[] CategoryIDs)>();

        foreach (var c in characters)
        {
            var item = new CharacterPanelItemViewModel
            {
                ID          = c.ID,
                Name        = c.Name,
                Description = c.Description,
                Categories  = string.Join(", ", categories.Where(cat => c.CategoryIDs.Contains(cat.ID)).Select(cat => cat.Name))
            };

            var stateValues = await characterRepository.GetCharacterStateValuesAsync(c.ID);

            foreach (var sv in stateValues)
            {
                var attr = categoryAttrs.FirstOrDefault(a => a.ID == sv.AttributeID);

                item.StateValues.Add
                (
                    new CharacterStateValueViewModel
                    {
                        Name  = attr?.DisplayName ?? attr?.Name ?? sv.AttributeID.ToString(),
                        Value = sv.Value
                    }
                );
            }

            var relations = await characterRepository.GetRelationsByCharacterAsync(c.ID);

            foreach (var r in relations)
            {
                var otherID = r.SourceCharacterID == c.ID ?
                                  r.TargetCharacterID :
                                  r.SourceCharacterID;
                var otherName = charLookup.TryGetValue(otherID, out var other) ?
                                    other.Name :
                                    $"ID:{otherID}";
                var direction = r.SourceCharacterID == c.ID ?
                                    "→" :
                                    "←";

                item.Relations.Add
                (
                    new CharacterRelationViewModel
                    {
                        Target      = otherName,
                        Type        = r.RelationType,
                        Description = r.Description ?? string.Empty,
                        Direction   = direction
                    }
                );
            }

            items.Add((item, c.CategoryIDs));
        }

        var grouped = items
                      .SelectMany
                      (it => it.CategoryIDs.Length > 0 ?
                                 it.CategoryIDs.Select(catID => (CatID: catID, it.Item)) :
                                 [(-1L, it.Item)]
                      )
                      .GroupBy(x => x.CatID)
                      .OrderBy(g => g.Key)
                      .ToList();

        var localGroups = new List<CharacterCategoryGroupViewModel>();
        foreach (var grp in grouped)
        {
            var groupName = grp.Key >= 0 && categoryLookup.TryGetValue(grp.Key, out var cat) ?
                                cat.Name :
                                Loc.Get("Character.Panel.Uncategorized");

            var group = new CharacterCategoryGroupViewModel
            {
                CategoryName = groupName
            };

            foreach (var (_, item) in grp)
                group.Items.Add(item);

            localGroups.Add(group);
        }

        CharacterPanel.SetGroups(localGroups);
    }

    private async Task RefreshMemoryPanelAsync()
    {
        if (CurrentSession is null || CurrentProject is null)
            return;

        var memories   = await memoryRepository.GetBySessionAsync(CurrentSession.ID, long.MaxValue);
        var scenes     = await sceneRepository.GetBySessionAsync(CurrentSession.ID);
        var characters = await characterRepository.GetBySessionAsync(CurrentSession.ID);

        var sceneLookup = scenes.ToDictionary(s => s.ID);
        var charLookup  = characters.ToDictionary(c => c.ID);

        var grouped = memories
                      .GroupBy(m => m.SceneID)
                      .Select
                      (g =>
                          {
                              var scene = sceneLookup.GetValueOrDefault(g.Key);
                              var label = scene is not null ?
                                              scene.TimeLabel :
                                              $"ID:{g.Key}";

                              return new
                              {
                                  Label       = label,
                                  TimelinePos = scene?.TimelinePosition ?? 0,
                                  Items       = g
                              };
                          }
                      )
                      .OrderBy(x => x.TimelinePos)
                      .ToList();

        var localGroups = new List<MemorySceneGroupViewModel>();
        foreach (var grp in grouped)
        {
            var group = new MemorySceneGroupViewModel
            {
                SceneLabel = grp.Label
            };

            foreach (var m in grp.Items)
            {
                var charNames = m.RelatedCharacterIDs
                                 .Where(charLookup.ContainsKey)
                                 .Select(id => charLookup[id].Name)
                                 .ToList();

                group.Items.Add
                (
                    new MemoryPanelItemViewModel
                    {
                        ID                   = m.ID,
                        Content              = m.Content,
                        TagsDisplay          = string.Join(", ", m.Tags),
                        SceneLabel           = grp.Label,
                        TimelinePos          = m.TimelinePos,
                        RelatedCharacters    = string.Join(", ", charNames),
                        HasRelatedCharacters = charNames.Count > 0,
                        UpdatedAtDisplay     = m.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm")
                    }
                );
            }

            localGroups.Add(group);
        }

        MemoryPanel.SetGroups(localGroups);

        Log.Information
        (
            "记忆面板刷新完成: 对话={SessionID}, 记忆数={Count}",
            CurrentSession.ID,
            MemoryPanel.Groups.Sum(g => g.Items.Count)
        );
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

            await memoryRepository.UpdateAsync(updated);

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
            await memoryRepository.DeleteAsync(item.ID);

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
