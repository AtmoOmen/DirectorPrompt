using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Agents;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Infrastructure.Extensions;
using DirectorPrompt.Localization;
using DirectorPrompt.Views;
using Microsoft.Extensions.DependencyInjection;
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
    IServiceProvider     serviceProvider,
    UserSettings         userSettings
)
    : ObservableObject
{
    private long pendingCorrectionOriginalRoundID;
    private long pendingCorrectionTempRoundID;
    private string? pendingCorrectionOriginalNarrative;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectSelected))]
    public partial Project? CurrentProject { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSessionSelected))]
    public partial Session? CurrentSession { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = Loc.Get("Status.Ready");

    [ObservableProperty]
    public partial bool IsProcessing { get; set; }

    [ObservableProperty]
    public partial bool IsSessionSidebarExpanded { get; set; } = true;

    public bool IsProjectSelected => CurrentProject is not null;

    public bool IsSessionSelected => CurrentSession is not null;

    public DialogViewModel Dialog { get; } = new();

    public DirectiveInputViewModel DirectiveInput { get; } = new();

    public StatePanelViewModel StatePanel { get; } = new();

    public DirectivesPanelViewModel DirectivesPanel { get; } = new();

    public CharacterPanelViewModel CharacterPanel { get; } = new();

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

            await characterRepository.CloneProjectCharactersToSessionAsync(CurrentProject.ID, session.ID);

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
    private void NewProject()
    {
        var window = serviceProvider.GetRequiredService<ProjectEditWindow>();
        window.Owner = GetCurrentWindow();

        if (window.ShowDialog() == true)
            _ = LoadProjectsAsync();
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

        IsProcessing  = true;
        StatusMessage = Loc.Get("Status.Processing");
        ResetPipelineStages();

        try
        {
            var items = DirectiveInput.Directives
                                      .Select(d => new DirectiveItem(d.Type, d.Content, d.Order, d.TTL))
                                      .ToList();

            var batch = new DirectiveBatch(CurrentProject.ID, items);

            Log.Information
            (
                "用户发送指令: 项目={ProjectID} ({ProjectName}), 对话={SessionID}, 指令数={Count}",
                CurrentProject.ID,
                CurrentProject.Name,
                CurrentSession.ID,
                items.Count
            );

            Dialog.AddDirectorEntry(0, items.Select(d => (d.Type, d.Content)).ToList());

            var streamingEntry = Dialog.BeginStreamingNarrative(0);

            var dispatcher = Application.Current.Dispatcher;

            Action<string, string>      streamingUpdate = (narrative, thinking) => dispatcher.BeginInvoke(new Action(() => { streamingEntry.UpdateStreamingContent(narrative, thinking); }));
            Action<PipelineStageUpdate> stageUpdate     = update => dispatcher.BeginInvoke(new Action(() => { UpdatePipelineStage(update.Stage, update.Status, update.Detail); }));

            var result = await orchestrator.ProcessBatchAsync(batch, CurrentSession.ID, streamingUpdate, stageUpdate);

            streamingEntry.RoundID  = result.RoundID;
            streamingEntry.Content  = result.Narrative;
            streamingEntry.Thinking = result.Thinking;
            streamingEntry.RenderMarkdown();

            Log.Information
            (
                "指令处理完成: 轮次={RoundID}, 审计通过={Passed}, 叙事长度={NarrativeLen}",
                result.RoundID,
                result.AuditPassed,
                result.Narrative.Length
            );

            if (result.Violations.Count > 0)
            {
                foreach (var v in result.Violations)
                    Log.Warning("  违规: [{Severity}] {Description}", v.Severity, v.Description);
            }

            await RefreshSidebarAsync();

            DirectiveInput.Clear();
            StatusMessage = result.AuditPassed ?
                                Loc.Get("Status.Complete") :
                                Loc.Get("Status.CompleteWithWarnings", result.Violations.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "处理指令失败");
            StatusMessage = Loc.Get("Status.ProcessFailed", ex.Message);
        }
        finally
        {
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

            var events        = await eventRepository.GetByRoundAsync(latestRound);
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
    private async Task RewriteLastRoundAsync()
    {
        if (CurrentSession is null)
            return;

        if (CurrentProject is null)
            return;

        IsProcessing  = true;
        StatusMessage = Loc.Get("Status.Rewriting");
        ResetPipelineStages();

        try
        {
            var latestRound = await eventRepository.GetLatestRoundIDAsync(CurrentSession.ID);

            if (latestRound <= 0)
            {
                StatusMessage = Loc.Get("Status.NoRoundToRewrite");
                return;
            }

            var events = await eventRepository.GetByRoundAsync(latestRound);
            var directorEvent = events.FirstOrDefault(e => e.Type == EventType.DirectorInput);

            if (directorEvent is null)
            {
                StatusMessage = Loc.Get("Status.OriginalDirectiveNotFound");
                return;
            }

            var directives = ParseDirectivesFromEvent(directorEvent.Data);

            if (directives.Count == 0)
            {
                StatusMessage = Loc.Get("Status.OriginalDirectiveNotFound");
                return;
            }

            var batch = new DirectiveBatch(CurrentProject.ID, directives);

            Dialog.RemoveEntriesByRound(latestRound);

            Dialog.AddDirectorEntry(0, directives.Select(d => (d.Type, d.Content)).ToList());

            var streamingEntry = Dialog.BeginStreamingNarrative(0);

            var dispatcher = Application.Current.Dispatcher;

            Action<string, string>      streamingUpdate = (narrative, thinking) => dispatcher.BeginInvoke(new Action(() => { streamingEntry.UpdateStreamingContent(narrative, thinking); }));
            Action<PipelineStageUpdate> stageUpdate     = update => dispatcher.BeginInvoke(new Action(() => { UpdatePipelineStage(update.Stage, update.Status, update.Detail); }));

            var result = await orchestrator.RewriteAsync(batch, CurrentSession.ID, streamingUpdate, stageUpdate);

            streamingEntry.RoundID  = result.RoundID;
            streamingEntry.Content  = result.Narrative;
            streamingEntry.Thinking = result.Thinking;
            streamingEntry.RenderMarkdown();

            await RefreshSidebarAsync();

            DirectiveInput.Clear();
            StatusMessage = result.AuditPassed ?
                                Loc.Get("Status.Complete") :
                                Loc.Get("Status.CompleteWithWarnings", result.Violations.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "重写失败");
            StatusMessage = Loc.Get("Status.RewriteFailed", ex.Message);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task CorrectLastRoundAsync()
    {
        if (CurrentSession is null)
            return;

        var latestRound = await eventRepository.GetLatestRoundIDAsync(CurrentSession.ID);

        if (latestRound <= 0)
        {
            StatusMessage = Loc.Get("Status.NoRoundToCorrect");
            return;
        }

        var guidance = PromptDialog.MultilineInput
        (
            GetCurrentWindow(),
            Loc.Get("Dialog.CorrectTitle"),
            Loc.Get("Dialog.CorrectPrompt"),
            string.Empty
        );

        if (string.IsNullOrWhiteSpace(guidance))
            return;

        IsProcessing  = true;
        StatusMessage = Loc.Get("Status.Correcting");
        ResetPipelineStages();

        try
        {
            var events = await eventRepository.GetByRoundAsync(latestRound);
            var narrativeEvent = events.FirstOrDefault(e => e.Type == EventType.NarrativeOutput);

            pendingCorrectionOriginalRoundID = latestRound;
            pendingCorrectionOriginalNarrative = narrativeEvent?.Data;

            var streamingEntry = Dialog.BeginStreamingNarrative(0);

            var dispatcher = Application.Current.Dispatcher;

            Action<string, string>      streamingUpdate = (narrative, thinking) => dispatcher.BeginInvoke(new Action(() => { streamingEntry.UpdateStreamingContent(narrative, thinking); }));
            Action<PipelineStageUpdate> stageUpdate     = update => dispatcher.BeginInvoke(new Action(() => { UpdatePipelineStage(update.Stage, update.Status, update.Detail); }));

            var result = await orchestrator.CorrectAsync
            (
                CurrentSession.ID,
                latestRound,
                guidance,
                streamingUpdate,
                stageUpdate
            );

            pendingCorrectionTempRoundID = result.RoundID;

            streamingEntry.RoundID  = result.RoundID;
            streamingEntry.Content  = result.Narrative;
            streamingEntry.Thinking = result.Thinking;
            streamingEntry.RenderMarkdown();

            await RefreshSidebarAsync();

            var accept = CorrectionCompareWindow.Show
            (
                GetCurrentWindow(),
                pendingCorrectionOriginalNarrative ?? string.Empty,
                result.Narrative,
                Loc.Get("Dialog.CorrectOriginal"),
                Loc.Get("Dialog.CorrectRevised")
            );

            if (accept)
            {
                StatusMessage = Loc.Get("Status.CommittingCorrection");

                await orchestrator.AcceptCorrectionAsync
                (
                    CurrentSession.ID,
                    pendingCorrectionOriginalRoundID,
                    pendingCorrectionTempRoundID
                );

                var originalNarrativeEntry = Dialog.Entries.FirstOrDefault
                (
                    e => e.RoundID == pendingCorrectionOriginalRoundID && e.IsNarrative
                );

                if (originalNarrativeEntry is not null)
                    Dialog.Entries.Remove(originalNarrativeEntry);

                streamingEntry.RoundID = pendingCorrectionOriginalRoundID;

                await RefreshSidebarAsync();
                StatusMessage = Loc.Get("Status.CorrectionAccepted");
            }
            else
            {
                StatusMessage = Loc.Get("Status.RejectingCorrection");

                await orchestrator.RejectCorrectionAsync
                (
                    CurrentSession.ID,
                    pendingCorrectionTempRoundID
                );

                Dialog.RemoveEntriesByRound(pendingCorrectionTempRoundID);

                await RefreshSidebarAsync();
                StatusMessage = Loc.Get("Status.CorrectionRejected");
            }

            pendingCorrectionOriginalRoundID = 0;
            pendingCorrectionTempRoundID     = 0;
            pendingCorrectionOriginalNarrative = null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "修正失败");
            StatusMessage = Loc.Get("Status.CorrectionFailed", ex.Message);
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

    partial void OnCurrentProjectChanged(Project? value)
    {
        CurrentSession = null;
        Sessions.Clear();
        Dialog.Clear();

        if (value is not null)
        {
            Log.Information("切换项目: ID={ProjectID}, 名称={Name}", value.ID, value.Name);
            _ = LoadSessionsAsync();
        }
    }

    partial void OnCurrentSessionChanged(Session? value)
    {
        Dialog.Clear();

        if (value is not null)
        {
            Log.Information("切换对话: ID={SessionID}", value.ID);

            _ = SaveSessionStateAsync();

            if (CurrentProject is not null && !string.IsNullOrWhiteSpace(CurrentProject.OpeningMessage))
                Dialog.AddOpeningMessage(CurrentProject.OpeningMessage);

            _ = LoadDialogHistoryAsync(value.ID);
            _ = RefreshSidebarAsync();
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

    private async Task LoadDialogHistoryAsync(long sessionID)
    {
        try
        {
            var events = await eventRepository.GetBySessionAsync(sessionID);

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
            var typeStr = element.GetProperty("type").GetString() ?? "Plot";
            var content = element.GetProperty("content").GetString() ?? string.Empty;

            var type = typeStr switch
            {
                "Tone"                => DirectiveType.Tone,
                "TemporaryConstraint" => DirectiveType.TemporaryConstraint,
                "SceneChange"         => DirectiveType.SceneChange,
                _                     => DirectiveType.Plot
            };

            result.Add(new DirectiveItem(type, content, order++, null));
        }

        return result;
    }

    private static IReadOnlyList<(DirectiveType Type, string Content)> ParseDirectorInputBlocks(string json)
    {
        var result = new List<(DirectiveType Type, string Content)>();

        try
        {
            using var doc = JsonDocument.Parse(json);

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var typeStr = element.GetProperty("type").GetString() ?? "Plot";
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

    private async Task RefreshSidebarAsync()
    {
        if (CurrentSession is null || CurrentProject is null)
            return;

        await RefreshStatePanelAsync();
        await RefreshDirectivesPanelAsync();
        await RefreshCharacterPanelAsync();
    }

    private async Task RefreshStatePanelAsync()
    {
        if (CurrentSession is null || CurrentProject is null)
            return;

        StatePanel.Clear();

        var scene = await sceneRepository.GetActiveSceneAsync(CurrentSession.ID);

        if (scene is not null)
        {
            StatePanel.CurrentSceneLabel = scene.TimeLabel;
            StatePanel.TimelineLabel     = scene.TimelinePosition.ToString();
        }

        var values     = await stateRepository.GetAllStateValuesAsync(CurrentProject.ID, CurrentSession.ID);
        var attributes = await stateRepository.GetAttributesAsync(CurrentProject.ID);

        foreach (var attr in attributes)
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
                    TtlLabel = d.TTL.HasValue ?
                                   Loc.Get("Directive.Panel.RemainingRounds", d.TTL) :
                                   Loc.Get("Directive.Panel.Permanent")
                }
            );
        }
    }

    private async Task RefreshCharacterPanelAsync()
    {
        if (CurrentSession is null)
            return;

        CharacterPanel.Clear();

        var characters = await characterRepository.GetBySessionAsync(CurrentSession.ID);

        foreach (var c in characters)
        {
            CharacterPanel.Characters.Add
            (
                new CharacterPanelItemViewModel
                {
                    Name        = c.Name,
                    Status      = c.Status.ToString(),
                    Description = c.Description
                }
            );
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
            PipelineStageKind.Audit               => "Pipeline.Stage.Audit",
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
