using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Agents;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DirectorPrompt.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly Orchestrator orchestrator;
    private readonly IProjectRepository projectRepository;
    private readonly IEventRepository eventRepository;
    private readonly ISceneRepository sceneRepository;
    private readonly IStateRepository stateRepository;
    private readonly ICharacterRepository characterRepository;
    private readonly IDirectiveRepository directiveRepository;
    private readonly IServiceProvider serviceProvider;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectSelected))]
    private Project? currentProject;

    [ObservableProperty]
    private string statusMessage = "就绪";

    [ObservableProperty]
    private bool isProcessing;

    public bool IsProjectSelected => CurrentProject is not null;

    public DialogViewModel Dialog { get; } = new();

    public DirectiveInputViewModel DirectiveInput { get; } = new();

    public StatePanelViewModel StatePanel { get; } = new();

    public DirectivesPanelViewModel DirectivesPanel { get; } = new();

    public CharacterPanelViewModel CharacterPanel { get; } = new();

    public ObservableCollection<Project> Projects { get; } = [];

    public MainViewModel(
        Orchestrator orchestrator,
        IProjectRepository projectRepository,
        IEventRepository eventRepository,
        ISceneRepository sceneRepository,
        IStateRepository stateRepository,
        ICharacterRepository characterRepository,
        IDirectiveRepository directiveRepository,
        IServiceProvider serviceProvider)
    {
        this.orchestrator = orchestrator;
        this.projectRepository = projectRepository;
        this.eventRepository = eventRepository;
        this.sceneRepository = sceneRepository;
        this.stateRepository = stateRepository;
        this.characterRepository = characterRepository;
        this.directiveRepository = directiveRepository;
        this.serviceProvider = serviceProvider;
    }

    [RelayCommand]
    private async Task LoadProjectsAsync()
    {
        try
        {
            Log.Information("加载项目列表");

            var projects = await projectRepository.GetAllAsync();

            var previousID = CurrentProject?.ID;

            Projects.Clear();

            foreach (var project in projects)
            {
                Projects.Add(project);
            }

            if (previousID.HasValue)
            {
                CurrentProject = Projects.FirstOrDefault(p => p.ID == previousID.Value);
            }

            Log.Information("项目列表加载完成: 数量={Count}", Projects.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载项目列表失败");
            StatusMessage = $"加载失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void NewProject()
    {
        var window = serviceProvider.GetRequiredService<ProjectEditWindow>();
        window.Owner = GetCurrentWindow();

        if (window.ShowDialog() == true)
        {
            _ = LoadProjectsAsync();
        }
    }

    [RelayCommand]
    private async Task EditProjectAsync()
    {
        if (CurrentProject is null)
        {
            return;
        }

        var window = serviceProvider.GetRequiredService<ProjectEditWindow>();
        await window.ViewModel.LoadFromProjectAsync(CurrentProject);
        window.Owner = GetCurrentWindow();

        if (window.ShowDialog() == true)
        {
            await LoadProjectsAsync();
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var window = serviceProvider.GetRequiredService<SettingsWindow>();
        window.Owner = GetCurrentWindow();
        window.ShowDialog();
    }

    private static Window? GetCurrentWindow() => Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive);

    [RelayCommand]
    private async Task SendDirectivesAsync()
    {
        if (CurrentProject is null)
        {
            StatusMessage = "请先选择或创建项目";
            return;
        }

        if (DirectiveInput.Directives.Count == 0)
        {
            StatusMessage = "请至少添加一条指令";
            return;
        }

        IsProcessing = true;
        StatusMessage = "处理中…";

        try
        {
            var items = DirectiveInput.Directives
                .Select(d => new DirectiveItem(d.Type, d.Content, d.Order))
                .ToList();

            var batch = new DirectiveBatch(CurrentProject.ID, items);

            var directorContent = string.Join("\n", items.Select(d => $"[{d.Type}] {d.Content}"));
            Dialog.AddDirectorEntry(0, directorContent);

            var streamingEntry = Dialog.BeginStreamingNarrative(0);

            var dispatcher = Application.Current.Dispatcher;

            var result = await orchestrator.ProcessBatchAsync
            (
                batch,
                onStreamingUpdate: (narrative, thinking) =>
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        streamingEntry.UpdateStreamingContent(narrative, thinking);
                    }));
                }
            );

            streamingEntry.RoundID = result.RoundID;
            streamingEntry.Content = result.Narrative;
            streamingEntry.Thinking = result.Thinking;
            streamingEntry.RenderMarkdown();

            await RefreshSidebarAsync();

            DirectiveInput.Clear();
            StatusMessage = result.AuditPassed ? "完成" : $"完成 (审计警告: {result.Violations.Count} 条)";
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "处理指令失败");
            StatusMessage = $"处理失败: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task DeleteLastRoundAsync()
    {
        if (CurrentProject is null)
        {
            return;
        }

        IsProcessing = true;
        StatusMessage = "回滚中…";

        try
        {
            var latestRound = await eventRepository.GetLatestRoundIDAsync(CurrentProject.ID);

            if (latestRound <= 0)
            {
                StatusMessage = "没有可回滚的轮次";
                return;
            }

            await orchestrator.DeleteRoundAsync(CurrentProject.ID, latestRound);

            Dialog.RemoveEntriesByRound(latestRound);

            await RefreshSidebarAsync();
            StatusMessage = "已回滚";
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "回滚失败");
            StatusMessage = $"回滚失败: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task RewriteLastRoundAsync()
    {
        if (CurrentProject is null)
        {
            return;
        }

        IsProcessing = true;
        StatusMessage = "重写中…";

        try
        {
            var latestRound = await eventRepository.GetLatestRoundIDAsync(CurrentProject.ID);

            if (latestRound <= 0)
            {
                StatusMessage = "没有可重写的轮次";
                return;
            }

            var events = await eventRepository.GetByRoundAsync(latestRound);
            var directorEvent = events.FirstOrDefault(e => e.Type == EventType.DirectorInput);

            if (directorEvent is null)
            {
                StatusMessage = "找不到原始指令";
                return;
            }

            StatusMessage = "重写功能需要原始指令批次, 请重新输入指令后发送";
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "重写失败");
            StatusMessage = $"重写失败: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    partial void OnCurrentProjectChanged(Project? value)
    {
        Dialog.Clear();

        if (value is not null)
        {
            Log.Information("切换项目: ID={ProjectID}, 名称={Name}", value.ID, value.Name);

            if (!string.IsNullOrWhiteSpace(value.OpeningMessage))
            {
                Dialog.AddOpeningMessage(value.OpeningMessage);
            }

            _ = RefreshSidebarAsync();
        }
    }

    private async Task RefreshSidebarAsync()
    {
        if (CurrentProject is null)
        {
            return;
        }

        await RefreshStatePanelAsync();
        await RefreshDirectivesPanelAsync();
        await RefreshCharacterPanelAsync();
    }

    private async Task RefreshStatePanelAsync()
    {
        if (CurrentProject is null)
        {
            return;
        }

        StatePanel.Clear();

        var scene = await sceneRepository.GetActiveSceneAsync(CurrentProject.ID);

        if (scene is not null)
        {
            StatePanel.CurrentSceneLabel = scene.TimeLabel;
            StatePanel.TimelineLabel = scene.TimelinePosition.ToString();
        }

        var values = await stateRepository.GetAllStateValuesAsync(CurrentProject.ID);
        var attributes = await stateRepository.GetAttributesAsync(CurrentProject.ID);

        foreach (var attr in attributes)
        {
            var value = values.FirstOrDefault(v => v.AttributeID == attr.ID);

            StatePanel.StateItems.Add(new StateItemViewModel
            {
                Name = attr.DisplayName,
                Value = value?.Value ?? "—",
                Scope = attr.Scope.ToString()
            });
        }

        var flags = await stateRepository.GetFlagsAsync(CurrentProject.ID);

        foreach (var flag in flags)
        {
            StatePanel.StateItems.Add(new StateItemViewModel
            {
                Name = flag.DisplayName,
                Value = flag.Value ? "✓" : "✗",
                Scope = "Flag"
            });
        }
    }

    private async Task RefreshDirectivesPanelAsync()
    {
        if (CurrentProject is null)
        {
            return;
        }

        DirectivesPanel.Clear();

        var directives = await directiveRepository.GetActiveAsync(CurrentProject.ID);

        foreach (var d in directives)
        {
            DirectivesPanel.Directives.Add(new DirectivePanelItemViewModel
            {
                Type = d.Type switch
                {
                    DirectiveType.Tone => "🎭",
                    DirectiveType.TemporaryConstraint => "🚫",
                    DirectiveType.SceneChange => "🎬",
                    _ => "📝"
                },
                Content = d.Content,
                HasTTL = d.TTL.HasValue,
                TtlLabel = d.TTL.HasValue ? $"剩余 {d.TTL} 轮" : "永久"
            });
        }
    }

    private async Task RefreshCharacterPanelAsync()
    {
        if (CurrentProject is null)
        {
            return;
        }

        CharacterPanel.Clear();

        var characters = await characterRepository.GetByProjectAsync(CurrentProject.ID);

        foreach (var c in characters)
        {
            CharacterPanel.Characters.Add(new CharacterPanelItemViewModel
            {
                Name = c.Name,
                Status = c.Status.ToString(),
                Description = c.Description
            });
        }
    }
}
