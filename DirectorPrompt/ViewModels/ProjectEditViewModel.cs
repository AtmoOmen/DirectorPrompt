using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Agents;
using DirectorPrompt.Agents.Retrieval;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using DirectorPrompt.Localization;
using Microsoft.Win32;
using Serilog;

namespace DirectorPrompt.ViewModels;

public sealed partial class ProjectEditViewModel
(
    IProjectRepository    projectRepository,
    IKnowledgeRepository  knowledgeRepository,
    IStateRepository      stateRepository,
    ICharacterRepository  characterRepository,
    IProjectPortService   projectPortService,
    EmbeddingIndexService embeddingIndexService,
    AgentConfigResolver   agentConfigResolver,
    UserSettings          userSettings
)
    : ObservableObject
{
    private long projectID;

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

        var dialog = new SaveFileDialog
        {
            Filter   = $"DirectorPrompt {Loc.Get("Project.Import.DirectorPrompt.Package")}|*.dppkg",
            FileName = $"{Name}.dppkg"
        };

        if (dialog.ShowDialog() != true)
            return;

        IsSaving = true;

        try
        {
            await projectPortService.ExportAsync(projectID, dialog.FileName);
            Log.Information("导出项目: ID={ProjectID}, 路径={Path}", projectID, dialog.FileName);
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
        projectID      = project.ID;
        Name           = project.Name;
        Description    = project.Description;
        OpeningMessage = project.OpeningMessage;

        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(TitleText));

        await LoadKnowledgeAsync();
        await LoadStateSystemAsync();
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

        var config = AttributeConfigSerializer.Deserialize<StateAttributeConfigDTO>(json);

        if (config is null)
            return;

        vm.MinValue    = config.Min;
        vm.MaxValue    = config.Max;
        vm.Unit        = config.Unit        ?? string.Empty;
        vm.ChangeRules = config.ChangeRules ?? string.Empty;

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

            var enterDirs = ToDirectiveItems(phase.EnterDirectives);
            var exitDirs  = ToDirectiveItems(phase.ExitDirectives);

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

    private static List<DirectiveItem> ToDirectiveItems(IReadOnlyList<DirectiveConfig> directives)
    {
        var result = new List<DirectiveItem>();
        var order  = 1;

        foreach (var d in directives)
            result.Add(new DirectiveItem(d.Type, d.Content, order++, d.TTL));

        return result;
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
    }

    private void RefreshAvailableNumericAttributes()
    {
        var globalNumericNames = StateAttributes
                                 .Where(a => a.ValueType == StateValueType.Numeric)
                                 .Select(a => a.Name)
                                 .ToList();

        foreach (var attr in StateAttributes)
        {
            if (attr.ValueType != StateValueType.Enum)
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
                if (attr.ValueType != StateValueType.Enum)
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

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Validate())
            return;

        IsSaving = true;

        try
        {
            var project = new Project
            {
                ID             = projectID,
                Name           = Name.Trim(),
                Description    = Description,
                OpeningMessage = OpeningMessage
            };

            await projectRepository.UpdateAsync(project);
            SavedProjectID = projectID;
            SaveSuccess    = true;

            foreach (var group in KnowledgeGroups)
            {
                await SaveKnowledgeGroupAsync(group);

                foreach (var entry in group.Entries)
                {
                    if (entry.ID > 0)
                        await SaveKnowledgeEntryAsync(entry);
                }
            }

            foreach (var attr in StateAttributes)
            {
                if (attr.ID > 0)
                    await SaveStateAttributeAsync(attr);
            }

            foreach (var category in CharacterCategories)
            {
                if (category.ID > 0)
                    await SaveCharacterCategoryAsync(category);

                foreach (var attr in category.StateAttributes)
                {
                    if (attr.ID > 0)
                        await SaveStateAttributeAsync(attr);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存项目失败");
            ValidationMessage = Loc.Get("Project.SaveFailed", ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
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

        var created = await knowledgeRepository.CreateAsync(entry);

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

            await knowledgeRepository.UpdateAsync(model);

            var stored = await knowledgeRepository.GetByIDAsync(model.ID) ??
                         throw new InvalidOperationException($"知识条目 {model.ID} 不存在");
            var embeddingConfig = agentConfigResolver.ResolveEmbedding(userSettings.EmbeddingConfig) ??
                                  throw new InvalidOperationException("向量模型配置无效: 未找到对应的提供商");

            await embeddingIndexService.IndexKnowledgeAsync([stored], embeddingConfig);
            ValidationMessage = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存知识条目失败");
            ValidationMessage = Loc.Get("Knowledge.Entry.SaveFailed", ex.Message);
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
            await knowledgeRepository.DeleteAsync(entry.ID);
            RemoveEntryFromGroups(entry);
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

        var created = await knowledgeRepository.CreateGroupAsync(group);

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

            await knowledgeRepository.UpdateGroupAsync(model);
            ValidationMessage = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存知识分组失败");
            ValidationMessage = Loc.Get("Knowledge.Group.SaveFailed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteKnowledgeGroupAsync(KnowledgeGroupEditViewModel group)
    {
        if (group.ID <= 0)
            return;

        try
        {
            await knowledgeRepository.DeleteGroupAsync(group.ID);
            KnowledgeGroups.Remove(group);
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
    }

    [RelayCommand]
    private async Task SaveStateAttributeAsync(StateAttributeEditViewModel attribute)
    {
        try
        {
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
                Driver = attribute.ValueType == StateValueType.Enum ?
                             Driver.System :
                             attribute.Driver,
                Config = attribute.BuildConfig()
            };

            await stateRepository.UpdateAttributeAsync(model);
            ValidationMessage = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存状态属性失败");
            ValidationMessage = Loc.Get("State.Attribute.SaveFailed", ex.Message);
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
            await stateRepository.DeleteAttributeAsync(attribute.ID);
            StateAttributes.Remove(attribute);
            RemoveAttributeFromCategory(attribute);
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

        var created = await characterRepository.CreateCategoryAsync(category);

        var vm = new CharacterCategoryEditViewModel();
        vm.SyncFromModel(created);
        CharacterCategories.Add(vm);

        RefreshAvailableParentCategories();
    }

    [RelayCommand]
    private async Task SaveCharacterCategoryAsync(CharacterCategoryEditViewModel category)
    {
        try
        {
            var model = category.ToModel(projectID);
            await characterRepository.UpdateCategoryAsync(model);
            ValidationMessage = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存分类失败");
            ValidationMessage = Loc.Get("Character.Category.SaveFailed", ex.Message);
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
            await characterRepository.DeleteCategoryAsync(category.ID);
            CharacterCategories.Remove(category);

            RefreshAvailableParentCategories();

            var categoryAttrs = StateAttributes
                                .Where(a => a.Scope == StateScope.Category && a.CategoryID == category.ID)
                                .ToList();

            foreach (var attr in categoryAttrs)
                StateAttributes.Remove(attr);
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

    private sealed record StateAttributeConfigDTO
    {
        public float?                      Min         { get; init; }
        public float?                      Max         { get; init; }
        public string?                     Unit        { get; init; }
        public string?                     ChangeRules { get; init; }
        public List<string>?               Options     { get; init; }
        public string?                     Trigger     { get; init; }
        public List<EnumTransitionConfig>? Transitions { get; init; }
        public List<Phase>                 Phases      { get; init; } = [];
    }
}
