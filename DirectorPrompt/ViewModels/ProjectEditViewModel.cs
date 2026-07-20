using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Agents;
using DirectorPrompt.Agents.Config;
using DirectorPrompt.Agents.Retrieval;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using DirectorPrompt.Localization;
using DirectorPrompt.Services;
using Serilog;

namespace DirectorPrompt.ViewModels;

public sealed partial class ProjectEditViewModel
(
    IKnowledgeRepository    knowledgeRepository,
    IStateRepository        stateRepository,
    ICharacterRepository    characterRepository,
    IProjectPortService     projectPortService,
    EmbeddingIndexService   embeddingIndexService,
    AgentConfigResolver     agentConfigResolver,
    UserSettings            userSettings,
    IFilePickerService      filePickerService,
    IProjectContentService? projectContentService = null
)
    : ObservableObject
{
    private long     projectID;
    private Project? savedProject;

    private readonly Dictionary<long, string> savedKnowledgeGroupStates    = [];
    private readonly Dictionary<long, string> savedKnowledgeEntryStates    = [];
    private readonly Dictionary<long, string> savedStateAttributeStates    = [];
    private readonly Dictionary<long, string> savedCharacterCategoryStates = [];

    private IProjectContentService ProjectContentService =>
        projectContentService ?? throw new InvalidOperationException("项目内容服务不可用");

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OpeningMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    public partial string ValidationMessage { get; set; } = string.Empty;

    public ObservableCollection<KnowledgeGroupEditViewModel> KnowledgeGroups { get; } = [];

    public ObservableCollection<StateAttributeEditViewModel> StateAttributes { get; } = [];

    public ObservableCollection<CharacterCategoryEditViewModel> CharacterCategories { get; } = [];

    public bool IsEditing => projectID > 0;

    public string TitleText => Loc.Get("Project.EditTitle");

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    public bool SaveSuccess { get; private set; }

    public long SavedProjectID { get; private set; }

    [RelayCommand]
    private async Task ExportProjectAsync()
    {
        if (projectID <= 0)
        {
            ValidationMessage = Loc.Get("Project.SaveBasicInfoFirst");
            return;
        }

        var fileName = await filePickerService.SaveAsync
                       (
                           $"DirectorPrompt {Loc.Get("Project.Import.DirectorPrompt.Package")}",
                           "*.dppkg",
                           $"{Name}.dppkg"
                       );

        if (fileName is null)
            return;

        IsSaving = true;

        try
        {
            await projectPortService.ExportAsync(projectID, fileName);
            Log.Information("导出项目: ID={ProjectID}, 路径={Path}", projectID, fileName);
            ValidationMessage = Loc.Get("Status.ExportComplete");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "导出项目失败");
            ValidationMessage = Loc.Get("Status.ExportFailed", ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    public async Task LoadFromProjectAsync(Project project)
    {
        Log.Information("开始加载项目编辑数据: 项目={ProjectID}", project.ID);

        projectID      = project.ID;
        Name           = project.Name;
        Description    = project.Description;
        OpeningMessage = project.OpeningMessage;

        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(TitleText));

        await LoadKnowledgeAsync();
        await LoadStateSystemAsync();
        CaptureSaveState(project);

        Log.Information
        (
            "项目编辑数据加载完成: 项目={ProjectID}, 知识分组数={KnowledgeGroupCount}, 全局属性数={StateAttributeCount}, 人物分类数={CategoryCount}",
            projectID,
            KnowledgeGroups.Count,
            StateAttributes.Count,
            CharacterCategories.Count
        );
    }

    private static KnowledgeEntryEditViewModel CreateEntryVM(KnowledgeEntry entry, string groupName) =>
        new()
        {
            ID           = entry.ID,
            Remarks      = entry.Remarks,
            Content      = entry.Content,
            Keywords     = string.Join(", ", entry.Keywords),
            GroupID      = entry.GroupID,
            Active       = entry.Active,
            GroupDisplay = groupName
        };

    private void ParseStateConfig(StateAttributeEditViewModel vm, string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return;

        var config = string.IsNullOrWhiteSpace(json) ?
                         null :
                         JsonSerializer.Deserialize<StateAttributeConfig>(json, JsonOptions.Default);

        if (config is null)
            return;

        vm.MinValue     = config.Min;
        vm.MaxValue     = config.Max;
        vm.InitialValue = config.Initial;
        vm.Unit         = config.Unit ?? string.Empty;
        vm.ChangeRules = vm.ValueType == StateValueType.Numeric ?
                             config.ChangeRules ?? string.Empty :
                             string.Empty;

        foreach (var change in config.NumericChanges)
        {
            vm.NumericChanges.Add
            (
                new NumericStateChangeRuleEditViewModel
                {
                    ID = string.IsNullOrWhiteSpace(change.ID) ?
                             Guid.NewGuid().ToString("N") :
                             change.ID,
                    Remarks          = change.Remarks,
                    AttributeName    = change.AttributeName,
                    Expression       = change.Expression,
                    ChangeExpression = change.ChangeExpression,
                    Trigger          = change.Trigger,
                    SwitchMode       = change.SwitchMode
                }
            );
        }

        if (config.Options is not null)
            vm.Options = string.Join(", ", config.Options);

        if (config.Trigger is not null && Enum.TryParse<SystemTrigger>(config.Trigger, out var t))
            vm.Trigger = t;

        if (config.Transitions is not null)
        {
            foreach (var tr in config.Transitions)
            {
                var existing = vm.Transitions.FirstOrDefault(x => x.Option == tr.Option);

                if (existing is not null)
                {
                    existing.Method        = tr.Method;
                    existing.ChangeRules   = tr.ChangeRules ?? string.Empty;
                    existing.Weight        = tr.Weight;
                    existing.AttributeName = tr.AttributeName;
                    existing.Expression    = tr.Expression;
                    existing.SwitchMode    = tr.SwitchMode;
                }
            }
        }

        foreach (var phase in config.Phases)
        {
            var phaseVM = new PhaseEditViewModel();
            phaseVM.PopulateAvailableKnowledge(KnowledgeGroups);

            var enterDirs = DirectiveItem.FromConfigs(phase.EnterDirectives);
            var exitDirs  = DirectiveItem.FromConfigs(phase.ExitDirectives);

            phaseVM.SyncFromConfig
            (
                phase.Name,
                phase.Expression,
                phase.KnowledgeIDs.ToArray(),
                phase.KnowledgeGroupIDs.ToArray(),
                enterDirs,
                exitDirs
            );

            vm.Phases.Add(phaseVM);
        }
    }

    private async Task LoadKnowledgeAsync()
    {
        if (projectID <= 0)
            return;

        KnowledgeGroups.Clear();

        var groups = await knowledgeRepository.GetGroupsAsync(projectID);

        foreach (var group in groups)
        {
            var entries = await knowledgeRepository.GetByGroupAsync(group.ID);
            var groupVM = new KnowledgeGroupEditViewModel
            {
                ID          = group.ID,
                Name        = group.Name,
                Description = group.Description ?? string.Empty,
                Active      = group.Active
            };

            foreach (var entry in entries.Where(e => e.GroupID == group.ID))
                groupVM.Entries.Add(CreateEntryVM(entry, group.Name));

            KnowledgeGroups.Add(groupVM);
        }

        Log.Debug
        (
            "项目知识数据加载完成: 项目={ProjectID}, 知识分组数={KnowledgeGroupCount}, 知识条目数={KnowledgeEntryCount}",
            projectID,
            KnowledgeGroups.Count,
            KnowledgeGroups.Sum(group => group.Entries.Count)
        );
    }

    private async Task LoadStateSystemAsync()
    {
        if (projectID <= 0)
            return;

        StateAttributes.Clear();
        CharacterCategories.Clear();

        var attributes = await stateRepository.GetAttributesAsync(projectID);
        var values     = await stateRepository.GetAllStateValuesAsync(projectID, 0);

        var categories = await characterRepository.GetCategoriesAsync(projectID);

        foreach (var cat in categories)
        {
            var vm = new CharacterCategoryEditViewModel();
            vm.SyncFromModel(cat);
            CharacterCategories.Add(vm);
        }

        RefreshAvailableParentCategories();

        foreach (var attr in attributes)
        {
            var value = values.FirstOrDefault(v => v.AttributeID == attr.ID);

            var attrVM = new StateAttributeEditViewModel
            {
                ID           = attr.ID,
                Name         = attr.Name,
                DisplayName  = attr.DisplayName,
                ValueType    = attr.ValueType,
                Driver       = attr.Driver,
                Scope        = attr.Scope,
                CategoryID   = attr.CategoryID,
                CurrentValue = value?.Value ?? string.Empty
            };

            ParseStateConfig(attrVM, attr.Config);

            if (attr is { Scope: StateScope.Category, CategoryID: not null })
            {
                var catVM = CharacterCategories.FirstOrDefault(c => c.ID == attr.CategoryID.Value);

                catVM?.StateAttributes.Add(attrVM);
            }
            else
                StateAttributes.Add(attrVM);
        }

        RefreshAvailableNumericAttributes();

        Log.Debug
        (
            "项目状态系统加载完成: 项目={ProjectID}, 全局属性数={GlobalStateAttributeCount}, 分类数={CategoryCount}, 分类属性数={CategoryStateAttributeCount}",
            projectID,
            StateAttributes.Count,
            CharacterCategories.Count,
            CharacterCategories.Sum(category => category.StateAttributes.Count)
        );
    }

    private void RefreshAvailableNumericAttributes()
    {
        var globalNumericNames = StateAttributes
                                 .Where(a => a.ValueType == StateValueType.Numeric)
                                 .Select(a => a.Name)
                                 .ToList();

        foreach (var attr in StateAttributes)
        {
            if (attr.ValueType != StateValueType.Enum && attr.ValueType != StateValueType.Numeric)
                continue;

            attr.AvailableNumericAttributes.Clear();

            foreach (var name in globalNumericNames.Where(n => n != attr.Name))
                attr.AvailableNumericAttributes.Add(name);
        }

        foreach (var cat in CharacterCategories)
        {
            var categoryNumericNames = cat.StateAttributes
                                          .Where(a => a.ValueType == StateValueType.Numeric)
                                          .Select(a => a.Name)
                                          .ToList();

            foreach (var attr in cat.StateAttributes)
            {
                if (attr.ValueType != StateValueType.Enum && attr.ValueType != StateValueType.Numeric)
                    continue;

                attr.AvailableNumericAttributes.Clear();

                foreach (var name in categoryNumericNames.Where(n => n != attr.Name))
                    attr.AvailableNumericAttributes.Add(name);
            }
        }
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationMessage = Loc.Get("Project.NameRequired");
            return false;
        }

        ValidationMessage = string.Empty;
        return true;
    }

    private void CaptureSaveState(Project project)
    {
        savedProject = project;
        savedKnowledgeGroupStates.Clear();
        savedKnowledgeEntryStates.Clear();
        savedStateAttributeStates.Clear();
        savedCharacterCategoryStates.Clear();

        foreach (var group in KnowledgeGroups)
        {
            savedKnowledgeGroupStates[group.ID] = GetKnowledgeGroupState(group);

            foreach (var entry in group.Entries)
                savedKnowledgeEntryStates[entry.ID] = GetKnowledgeEntryState(entry);
        }

        foreach (var attribute in StateAttributes)
            savedStateAttributeStates[attribute.ID] = GetStateAttributeState(attribute);

        foreach (var category in CharacterCategories)
        {
            savedCharacterCategoryStates[category.ID] = GetCharacterCategoryState(category);

            foreach (var attribute in category.StateAttributes)
                savedStateAttributeStates[attribute.ID] = GetStateAttributeState(attribute);
        }
    }

    private bool HasProjectChanges() =>
        savedProject is null                       ||
        savedProject.Name           != Name.Trim() ||
        savedProject.Description    != Description ||
        savedProject.OpeningMessage != OpeningMessage;

    private static string GetKnowledgeGroupState(KnowledgeGroupEditViewModel group) =>
        JsonSerializer.Serialize
        (
            new { group.Name, group.Description, group.Active },
            JsonOptions.Compact
        );

    private static string GetKnowledgeEntryState(KnowledgeEntryEditViewModel entry) =>
        JsonSerializer.Serialize
        (
            new { entry.Remarks, entry.Content, entry.Keywords, entry.GroupID, entry.Active },
            JsonOptions.Compact
        );

    private static string GetStateAttributeState(StateAttributeEditViewModel attribute) =>
        JsonSerializer.Serialize
        (
            new
            {
                attribute.Name,
                attribute.DisplayName,
                attribute.Scope,
                attribute.CategoryID,
                attribute.ValueType,
                attribute.Driver,
                Config = attribute.BuildConfig()
            },
            JsonOptions.Compact
        );

    private string GetCharacterCategoryState(CharacterCategoryEditViewModel category) =>
        JsonSerializer.Serialize(category.ToModel(projectID), JsonOptions.Compact);

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Validate())
            return;

        SaveSuccess = false;
        IsSaving    = true;

        Log.Information
        (
            "开始保存项目编辑数据: 项目={ProjectID}, 是否新建={IsNewProject}, 知识分组数={KnowledgeGroupCount}, 全局属性数={StateAttributeCount}, 分类数={CategoryCount}",
            projectID,
            projectID <= 0,
            KnowledgeGroups.Count,
            StateAttributes.Count,
            CharacterCategories.Count
        );

        try
        {
            using (ProjectContentService.BeginChangeBatch())
            {
                if (HasProjectChanges())
                {
                    var project = new Project
                    {
                        ID             = projectID,
                        Name           = Name.Trim(),
                        Description    = Description,
                        OpeningMessage = OpeningMessage
                    };

                    await ProjectContentService.UpdateProjectAsync(project);
                    savedProject = project;
                }

                foreach (var group in KnowledgeGroups)
                {
                    var groupState = GetKnowledgeGroupState(group);

                    if (!savedKnowledgeGroupStates.TryGetValue(group.ID, out var savedGroupState) || savedGroupState != groupState)
                    {
                        await SaveKnowledgeGroupAsync(group);
                        savedKnowledgeGroupStates[group.ID] = groupState;
                    }

                    foreach (var entry in group.Entries)
                    {
                        if (entry.ID <= 0)
                            continue;

                        var entryState = GetKnowledgeEntryState(entry);

                        if (savedKnowledgeEntryStates.TryGetValue(entry.ID, out var savedEntryState) && savedEntryState == entryState)
                            continue;

                        await SaveKnowledgeEntryAsync(entry);
                        savedKnowledgeEntryStates[entry.ID] = entryState;
                    }
                }

                foreach (var attribute in StateAttributes)
                {
                    if (!await SaveStateAttributeIfChangedAsync(attribute))
                        return;
                }

                foreach (var category in CharacterCategories)
                {
                    var categoryState = GetCharacterCategoryState(category);

                    if (!savedCharacterCategoryStates.TryGetValue(category.ID, out var savedCategoryState) || savedCategoryState != categoryState)
                    {
                        await SaveCharacterCategoryAsync(category);
                        savedCharacterCategoryStates[category.ID] = categoryState;
                    }

                    foreach (var attribute in category.StateAttributes)
                    {
                        if (!await SaveStateAttributeIfChangedAsync(attribute))
                            return;
                    }
                }

                ProjectContentService.NotifyProjectChanged(projectID);
            }

            SavedProjectID = projectID;
            SaveSuccess    = true;

            Log.Information
            (
                "项目编辑数据保存完成: 项目={ProjectID}, 知识分组数={KnowledgeGroupCount}, 全局属性数={StateAttributeCount}, 分类数={CategoryCount}",
                projectID,
                KnowledgeGroups.Count,
                StateAttributes.Count,
                CharacterCategories.Count
            );
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存项目失败");

            if (string.IsNullOrEmpty(ValidationMessage))
                ValidationMessage = Loc.Get("Project.SaveFailed", ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task<bool> SaveStateAttributeIfChangedAsync(StateAttributeEditViewModel attribute)
    {
        if (attribute.ID <= 0)
            return true;

        var attributeState = GetStateAttributeState(attribute);

        if (savedStateAttributeStates.TryGetValue(attribute.ID, out var savedAttributeState) && savedAttributeState == attributeState)
            return true;

        if (!await SaveStateAttributeAsync(attribute))
            return false;

        savedStateAttributeStates[attribute.ID] = attributeState;
        return true;
    }

    [RelayCommand]
    private async Task AddKnowledgeEntryAsync(KnowledgeGroupEditViewModel? group)
    {
        if (projectID <= 0)
        {
            ValidationMessage = Loc.Get("Project.SaveBasicInfoFirst");
            return;
        }

        var entry = new KnowledgeEntry
        {
            ProjectID = projectID,
            Remarks   = Loc.Get("Knowledge.Entry.New"),
            Content   = string.Empty,
            Keywords  = [],
            GroupID   = group?.ID,
            Active    = true
        };

        var created = await ProjectContentService.ManageKnowledgeEntryAsync
                      (
                          projectID,
                          ProjectContentAction.Create,
                          entry,
                          null
                      );

        var entryVM = new KnowledgeEntryEditViewModel
        {
            ID           = created.ID,
            Remarks      = created.Remarks,
            Content      = created.Content,
            Keywords     = string.Empty,
            GroupID      = created.GroupID,
            Active       = true,
            GroupDisplay = group?.Name ?? string.Empty,
            IsEditing    = true
        };

        group?.Entries.Add(entryVM);
        Log.Information("已新增知识条目: 项目={ProjectID}, 知识条目={KnowledgeEntryID}, 知识分组={KnowledgeGroupID}", projectID, created.ID, group?.ID);
    }

    [RelayCommand]
    private async Task SaveKnowledgeEntryAsync(KnowledgeEntryEditViewModel entry)
    {
        try
        {
            var keywords = entry.Keywords
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var model = new KnowledgeEntry
            {
                ID        = entry.ID,
                ProjectID = projectID,
                Remarks   = entry.Remarks,
                Content   = entry.Content,
                Keywords  = keywords,
                GroupID   = entry.GroupID,
                Active    = entry.Active
            };

            await ProjectContentService.ManageKnowledgeEntryAsync
            (
                projectID,
                ProjectContentAction.Update,
                model,
                model.ID
            );

            var stored = await knowledgeRepository.GetByIDAsync(model.ID) ??
                         throw new InvalidOperationException($"知识条目 {model.ID} 不存在");
            var embeddingConfig = agentConfigResolver.ResolveEmbedding(userSettings.EmbeddingConfig) ??
                                  throw new InvalidOperationException("向量模型配置无效: 未找到对应的提供商");

            await embeddingIndexService.IndexKnowledgeAsync([stored], embeddingConfig);
            ValidationMessage = string.Empty;
            Log.Information
            (
                "知识条目已保存并建立索引: 项目={ProjectID}, 知识条目={KnowledgeEntryID}, 内容长度={ContentLength}, 关键词数={KeywordCount}",
                projectID,
                model.ID,
                model.Content.Length,
                model.Keywords.Length
            );
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存知识条目失败");
            ValidationMessage = Loc.Get("Knowledge.Entry.SaveFailed", ex.Message);
            throw;
        }
    }

    [RelayCommand]
    private async Task DeleteKnowledgeEntryAsync(KnowledgeEntryEditViewModel entry)
    {
        if (entry.ID <= 0)
        {
            RemoveEntryFromGroups(entry);
            return;
        }

        try
        {
            await ProjectContentService.ManageKnowledgeEntryAsync
            (
                projectID,
                ProjectContentAction.Delete,
                null,
                entry.ID
            );
            RemoveEntryFromGroups(entry);
            Log.Information("知识条目已删除: 项目={ProjectID}, 知识条目={KnowledgeEntryID}", projectID, entry.ID);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除知识条目失败");
            ValidationMessage = Loc.Get("Common.DeleteFailed", ex.Message);
        }
    }

    private void RemoveEntryFromGroups(KnowledgeEntryEditViewModel entry)
    {
        foreach (var group in KnowledgeGroups)
        {
            var found = group.Entries.FirstOrDefault(e => e.ID == entry.ID);

            if (found is not null)
            {
                group.Entries.Remove(found);
                return;
            }
        }
    }

    [RelayCommand]
    private async Task AddKnowledgeGroupAsync()
    {
        if (projectID <= 0)
        {
            ValidationMessage = Loc.Get("Project.SaveBasicInfoFirst");
            return;
        }

        var group = new KnowledgeGroup
        {
            ProjectID   = projectID,
            Name        = Loc.Get("Knowledge.Group.New"),
            Description = string.Empty,
            Active      = true
        };

        var created = await ProjectContentService.ManageKnowledgeGroupAsync
                      (
                          projectID,
                          ProjectContentAction.Create,
                          group,
                          null
                      );

        KnowledgeGroups.Add
        (
            new KnowledgeGroupEditViewModel
            {
                ID          = created.ID,
                Name        = created.Name,
                Description = created.Description ?? string.Empty,
                Active      = created.Active
            }
        );
        Log.Information("已新增知识分组: 项目={ProjectID}, 知识分组={KnowledgeGroupID}", projectID, created.ID);
    }

    [RelayCommand]
    private async Task SaveKnowledgeGroupAsync(KnowledgeGroupEditViewModel group)
    {
        try
        {
            var model = new KnowledgeGroup
            {
                ID          = group.ID,
                ProjectID   = projectID,
                Name        = group.Name,
                Description = group.Description,
                Active      = group.Active
            };

            await ProjectContentService.ManageKnowledgeGroupAsync
            (
                projectID,
                ProjectContentAction.Update,
                model,
                model.ID
            );
            ValidationMessage = string.Empty;
            Log.Information("知识分组已保存: 项目={ProjectID}, 知识分组={KnowledgeGroupID}", projectID, model.ID);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存知识分组失败");
            ValidationMessage = Loc.Get("Knowledge.Group.SaveFailed", ex.Message);
            throw;
        }
    }

    [RelayCommand]
    private async Task DeleteKnowledgeGroupAsync(KnowledgeGroupEditViewModel group)
    {
        if (group.ID <= 0)
            return;

        try
        {
            await ProjectContentService.ManageKnowledgeGroupAsync
            (
                projectID,
                ProjectContentAction.Delete,
                null,
                group.ID
            );
            KnowledgeGroups.Remove(group);
            Log.Information("知识分组已删除: 项目={ProjectID}, 知识分组={KnowledgeGroupID}", projectID, group.ID);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除知识分组失败");
            ValidationMessage = Loc.Get("Knowledge.Group.DeleteFailed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task AddStateAttributeAsync()
    {
        if (projectID <= 0)
        {
            ValidationMessage = Loc.Get("Project.SaveBasicInfoFirst");
            return;
        }

        var attribute = new StateAttribute
        {
            ProjectID   = projectID,
            Name        = "new_attribute",
            DisplayName = Loc.Get("State.Attribute.New"),
            Scope       = StateScope.Global,
            ValueType   = StateValueType.Numeric,
            Driver      = Driver.Narrative,
            Config      = "{}"
        };

        var created = await stateRepository.CreateAttributeAsync(attribute);

        StateAttributes.Add
        (
            new StateAttributeEditViewModel
            {
                ID          = created.ID,
                Name        = created.Name,
                DisplayName = created.DisplayName,
                ValueType   = created.ValueType,
                Driver      = created.Driver,
                Scope       = created.Scope,
                CategoryID  = created.CategoryID,
                IsEditing   = true
            }
        );

        RefreshAvailableNumericAttributes();
        Log.Information("已新增全局状态属性: 项目={ProjectID}, 属性={StateAttributeID}, 名称={StateAttributeName}", projectID, created.ID, created.Name);
    }

    private async Task<bool> SaveStateAttributeAsync(StateAttributeEditViewModel attribute)
    {
        try
        {
            if (attribute.MinValue is not null && attribute.MaxValue is not null && attribute.MinValue > attribute.MaxValue)
            {
                ValidationMessage = Loc.Get("State.Attribute.InvalidRange");
                return false;
            }

            if (attribute.InitialValue is not null &&
                ((attribute.MinValue is not null && attribute.InitialValue < attribute.MinValue) ||
                 (attribute.MaxValue is not null && attribute.InitialValue > attribute.MaxValue)))
            {
                ValidationMessage = Loc.Get("State.Attribute.InitialValueOutOfRange");
                return false;
            }

            if (attribute.NumericChanges.Any(change => string.IsNullOrWhiteSpace(change.Expression) || string.IsNullOrWhiteSpace(change.ChangeExpression)))
            {
                ValidationMessage = Loc.Get("State.NumericChange.Required");
                return false;
            }

            var model = new StateAttribute
            {
                ID          = attribute.ID,
                ProjectID   = projectID,
                Name        = attribute.Name,
                DisplayName = attribute.DisplayName,
                Scope       = attribute.Scope,
                CategoryID = attribute.Scope == StateScope.Category ?
                                 attribute.CategoryID :
                                 null,
                ValueType = attribute.ValueType,
                Driver    = attribute.Driver,
                Config    = attribute.BuildConfig()
            };

            await stateRepository.UpdateAttributeAsync(model);
            ValidationMessage = string.Empty;
            Log.Information
            (
                "状态属性已保存: 项目={ProjectID}, 属性={StateAttributeID}, 名称={StateAttributeName}, 值类型={ValueType}, 范围={Scope}",
                projectID,
                model.ID,
                model.Name,
                model.ValueType,
                model.Scope
            );
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存状态属性失败");
            ValidationMessage = Loc.Get("State.Attribute.SaveFailed", ex.Message);
            return false;
        }
    }

    [RelayCommand]
    private async Task DeleteStateAttributeAsync(StateAttributeEditViewModel attribute)
    {
        if (attribute.ID <= 0)
        {
            StateAttributes.Remove(attribute);
            RemoveAttributeFromCategory(attribute);
            return;
        }

        try
        {
            await ProjectContentService.ManageStateAttributeAsync
            (
                projectID,
                ProjectContentAction.Delete,
                null,
                attribute.ID
            );
            StateAttributes.Remove(attribute);
            RemoveAttributeFromCategory(attribute);
            Log.Information("状态属性已删除: 项目={ProjectID}, 属性={StateAttributeID}", projectID, attribute.ID);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除状态属性失败");
            ValidationMessage = Loc.Get("Common.DeleteFailed", ex.Message);
        }
    }

    private void RemoveAttributeFromCategory(StateAttributeEditViewModel attribute)
    {
        if (attribute.Scope != StateScope.Category || attribute.CategoryID is null)
            return;

        var catVM = CharacterCategories.FirstOrDefault(c => c.ID == attribute.CategoryID.Value);

        catVM?.StateAttributes.Remove(attribute);
    }

    private void RefreshAvailableParentCategories()
    {
        foreach (var cat in CharacterCategories)
            cat.PopulateAvailableParentCategories(CharacterCategories);
    }

    [RelayCommand]
    private async Task AddCharacterCategoryAsync()
    {
        if (projectID <= 0)
        {
            ValidationMessage = Loc.Get("Project.SaveBasicInfoFirst");
            return;
        }

        var category = new CharacterCategory
        {
            ProjectID = projectID,
            Name      = Loc.Get("Character.Category.New")
        };

        var created = await ProjectContentService.ManageCharacterCategoryAsync
                      (
                          projectID,
                          ProjectContentAction.Create,
                          category,
                          null
                      );

        var vm = new CharacterCategoryEditViewModel();
        vm.SyncFromModel(created);
        CharacterCategories.Add(vm);

        RefreshAvailableParentCategories();
        Log.Information("已新增人物分类: 项目={ProjectID}, 分类={CategoryID}", projectID, created.ID);
    }

    [RelayCommand]
    private async Task SaveCharacterCategoryAsync(CharacterCategoryEditViewModel category)
    {
        try
        {
            var model = category.ToModel(projectID);
            await characterRepository.UpdateCategoryAsync(model);
            ValidationMessage = string.Empty;
            Log.Information("人物分类已保存: 项目={ProjectID}, 分类={CategoryID}, 父分类数={ParentCategoryCount}", projectID, model.ID, model.ParentCategoryIDs.Length);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存分类失败");
            ValidationMessage = Loc.Get("Character.Category.SaveFailed", ex.Message);
            throw;
        }
    }

    [RelayCommand]
    private async Task DeleteCharacterCategoryAsync(CharacterCategoryEditViewModel category)
    {
        if (category.ID <= 0)
        {
            CharacterCategories.Remove(category);
            return;
        }

        try
        {
            await ProjectContentService.ManageCharacterCategoryAsync
            (
                projectID,
                ProjectContentAction.Delete,
                null,
                category.ID
            );
            CharacterCategories.Remove(category);

            RefreshAvailableParentCategories();
            RefreshAvailableNumericAttributes();
            Log.Information("人物分类已删除: 项目={ProjectID}, 分类={CategoryID}", projectID, category.ID);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除分类失败");
            ValidationMessage = Loc.Get("Character.Category.DeleteFailed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task AddCategoryStateAttributeAsync(CharacterCategoryEditViewModel? category)
    {
        if (projectID <= 0)
        {
            ValidationMessage = Loc.Get("Project.SaveBasicInfoFirst");
            return;
        }

        if (category is null)
        {
            ValidationMessage = Loc.Get("Character.StateAttribute.SelectCategory");
            return;
        }

        var attribute = new StateAttribute
        {
            ProjectID   = projectID,
            Name        = "new_category_attribute",
            DisplayName = Loc.Get("State.Attribute.New"),
            Scope       = StateScope.Category,
            CategoryID  = category.ID,
            ValueType   = StateValueType.Numeric,
            Driver      = Driver.Narrative,
            Config      = "{}"
        };

        var created = await stateRepository.CreateAttributeAsync(attribute);

        var attrVM = new StateAttributeEditViewModel
        {
            ID          = created.ID,
            Name        = created.Name,
            DisplayName = created.DisplayName,
            ValueType   = created.ValueType,
            Driver      = created.Driver,
            Scope       = created.Scope,
            CategoryID  = created.CategoryID,
            IsEditing   = true
        };

        category.StateAttributes.Add(attrVM);

        RefreshAvailableNumericAttributes();
        Log.Information("已新增分类状态属性: 项目={ProjectID}, 分类={CategoryID}, 属性={StateAttributeID}, 名称={StateAttributeName}", projectID, category.ID, created.ID, created.Name);
    }

    [RelayCommand]
    private void AddPhase(StateAttributeEditViewModel? attribute)
    {
        if (attribute is null)
            return;

        var phase = new PhaseEditViewModel { Name = Loc.Get("Phase.New"), IsEditing = true };
        phase.PopulateAvailableKnowledge(KnowledgeGroups);
        attribute.Phases.Add(phase);
    }

    [RelayCommand]
    private void DeletePhase(PhaseEditViewModel? phase)
    {
        if (phase is null)
            return;

        foreach (var attr in StateAttributes)
        {
            if (attr.Phases.Remove(phase))
                return;
        }

        foreach (var cat in CharacterCategories)
        {
            foreach (var attr in cat.StateAttributes)
            {
                if (attr.Phases.Remove(phase))
                    return;
            }
        }
    }

    [RelayCommand]
    private void AddNumericChange(StateAttributeEditViewModel? attribute)
    {
        if (attribute is not null)
            attribute.NumericChanges.Add(new NumericStateChangeRuleEditViewModel());
    }

    [RelayCommand]
    private void DeleteNumericChange(NumericStateChangeRuleEditViewModel? change)
    {
        if (change is null)
            return;

        foreach (var attribute in StateAttributes.Concat(CharacterCategories.SelectMany(category => category.StateAttributes)))
        {
            if (attribute.NumericChanges.Remove(change))
                return;
        }
    }
}
