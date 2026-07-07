using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirectorPrompt.Agents;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using DirectorPrompt.Localization;
using Serilog;

namespace DirectorPrompt.ViewModels;

public sealed partial class ProjectEditViewModel : ObservableObject
{
    private readonly IProjectRepository     projectRepository;
    private readonly IKnowledgeRepository   knowledgeRepository;
    private readonly IStateRepository       stateRepository;
    private readonly ICharacterRepository   characterRepository;
    private readonly IModelConnectionTester connectionTester;

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

    public EmbeddingSettingViewModel Embedding { get; } = new();

    public AuditSettingViewModel Audit { get; } = new();

    public MemorySettingViewModel Memory { get; } = new();

    public KnowledgeSettingViewModel Knowledge { get; } = new();

    public bool IsEditing => projectID > 0;

    public string TitleText => Loc.Get("Project.EditTitle");

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    public bool SaveSuccess { get; private set; }

    public long SavedProjectID { get; private set; }

public ProjectEditViewModel
(
IProjectRepository     projectRepository,
IKnowledgeRepository   knowledgeRepository,
IStateRepository       stateRepository,
ICharacterRepository   characterRepository,
IModelConnectionTester connectionTester
)
{
this.projectRepository   = projectRepository;
this.knowledgeRepository = knowledgeRepository;
this.stateRepository     = stateRepository;
this.characterRepository = characterRepository;
        this.connectionTester    = connectionTester;
    }

    public async Task LoadFromProjectAsync(Project project)
    {
        projectID      = project.ID;
        Name           = project.Name;
        Description    = project.Description;
        OpeningMessage = project.OpeningMessage;

        LoadEmbeddingConfig(project.EmbeddingConfig);
        LoadAuditConfig(project.AuditConfig);
        LoadMemoryConfig(project.MemoryConfig);
        LoadKnowledgeConfig(project.KnowledgeConfig);

        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(TitleText));

        await LoadKnowledgeAsync();
        await LoadStateSystemAsync();
    }

    private void LoadEmbeddingConfig(string json)
    {
        var config = JsonSerializer.Deserialize<ModelConfig>(json) ?? new ModelConfig();

        Embedding.Provider  = config.Provider;
        Embedding.Endpoint  = config.Endpoint;
        Embedding.APIKey    = config.APIKey ?? string.Empty;
        Embedding.ModelName = config.ModelName;
    }

    private void LoadAuditConfig(string json)
    {
        var config = JsonSerializer.Deserialize<AuditConfig>(json) ?? new AuditConfig();

        Audit.Mode       = config.Mode;
        Audit.MaxRetries = config.MaxRetries;
    }

    private void LoadMemoryConfig(string json)
    {
        var config = JsonSerializer.Deserialize<MemoryConfig>(json) ?? new MemoryConfig();

        Memory.RecallTopK      = config.RecallTopK;
        Memory.TokenBudget     = config.TokenBudget;
        Memory.MinRelevance    = config.MinRelevance;
        Memory.TimeDecayLambda = config.TimeDecayLambda;
    }

    private void LoadKnowledgeConfig(string json)
    {
        var config = JsonSerializer.Deserialize<KnowledgeRetrievalConfig>(json) ?? new KnowledgeRetrievalConfig();

        Knowledge.SemanticTopK = config.SemanticTopK;
        Knowledge.TokenBudget  = config.TokenBudget;
        Knowledge.MinRelevance = config.MinRelevance;
    }

    private string BuildEmbeddingConfig()
    {
        var config = new ModelConfig
        {
            Provider  = Embedding.Provider,
            Endpoint  = Embedding.Endpoint,
            APIKey    = Embedding.APIKey,
            ModelName = Embedding.ModelName
        };

        return JsonSerializer.Serialize(config);
    }

    private string BuildAuditConfig()
    {
        var config = new AuditConfig
        {
            Mode       = Audit.Mode,
            MaxRetries = Audit.MaxRetries
        };

        return JsonSerializer.Serialize(config);
    }

    private string BuildMemoryConfig()
    {
        var config = new MemoryConfig
        {
            RecallTopK      = Memory.RecallTopK,
            TokenBudget     = Memory.TokenBudget,
            MinRelevance    = Memory.MinRelevance,
            TimeDecayLambda = Memory.TimeDecayLambda
        };

        return JsonSerializer.Serialize(config);
    }

    private string BuildKnowledgeConfig()
    {
        var config = new KnowledgeRetrievalConfig
        {
            SemanticTopK = Knowledge.SemanticTopK,
            TokenBudget  = Knowledge.TokenBudget,
            MinRelevance = Knowledge.MinRelevance
        };

        return JsonSerializer.Serialize(config);
    }

    private static KnowledgeEntryEditViewModel CreateEntryVM(KnowledgeEntry entry, string groupName) =>
        new()
        {
            ID           = entry.ID,
            Title        = entry.Title,
            Content      = entry.Content,
            Tags         = string.Join(", ", entry.Tags),
            GroupID      = entry.GroupID,
            Active       = entry.Active,
            GroupDisplay = groupName
        };

    private void ParseStateConfig(StateAttributeEditViewModel vm, string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return;

        try
        {
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("min", out var min))
            {
                vm.MinValue = min.ValueKind == JsonValueKind.Null ?
                                  null :
                                  min.GetSingle();
            }

            if (doc.RootElement.TryGetProperty("max", out var max))
            {
                vm.MaxValue = max.ValueKind == JsonValueKind.Null ?
                                  null :
                                  max.GetSingle();
            }

            if (doc.RootElement.TryGetProperty("unit", out var unit) && unit.ValueKind != JsonValueKind.Null)
                vm.Unit = unit.GetString() ?? string.Empty;

            if (doc.RootElement.TryGetProperty("changeRules", out var rules) && rules.ValueKind != JsonValueKind.Null)
                vm.ChangeRules = rules.GetString() ?? string.Empty;

            if (doc.RootElement.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                vm.Options = string.Join(", ", opts.EnumerateArray().Select(o => o.GetString() ?? string.Empty));

            if (doc.RootElement.TryGetProperty("trigger", out var trigger) && trigger.ValueKind != JsonValueKind.Null)
            {
                if (Enum.TryParse<SystemTrigger>(trigger.GetString(), out var t))
                    vm.Trigger = t;
            }

            if (doc.RootElement.TryGetProperty("generationGuide", out var guide) && guide.ValueKind != JsonValueKind.Null)
                vm.GenerationGuide = guide.GetString() ?? string.Empty;

            if (doc.RootElement.TryGetProperty("regenerateTrigger", out var regen) && regen.ValueKind != JsonValueKind.Null)
            {
                if (Enum.TryParse<SystemTrigger>(regen.GetString(), out var rt))
                    vm.RegenerateTrigger = rt;
            }

            if (doc.RootElement.TryGetProperty("phases", out var phasesEl) && phasesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var ph in phasesEl.EnumerateArray())
                {
                    var phaseVM = new PhaseEditViewModel();
                    phaseVM.PopulateAvailableKnowledge(KnowledgeGroups);

                    var phName = ph.TryGetProperty("name", out var pn) && pn.ValueKind != JsonValueKind.Null
                                     ? pn.GetString() ?? string.Empty
                                     : string.Empty;

                    var phExpr = ph.TryGetProperty("expression", out var pe) && pe.ValueKind != JsonValueKind.Null
                                     ? pe.GetString() ?? string.Empty
                                     : string.Empty;

                    var kIds = ph.TryGetProperty("knowledgeIds", out var kid) && kid.ValueKind == JsonValueKind.Array
                                   ? kid.EnumerateArray().Select(v => v.GetInt64()).ToArray()
                                   : [];
                    var gIds = ph.TryGetProperty("knowledgeGroupIds", out var gid) && gid.ValueKind == JsonValueKind.Array
                                   ? gid.EnumerateArray().Select(v => v.GetInt64()).ToArray()
                                   : [];

                    var enterDirs = ParsePhaseDirectives(ph, "enterDirectives");
                    var exitDirs = ParsePhaseDirectives(ph, "exitDirectives");

                    phaseVM.SyncFromConfig(phName, phExpr, kIds, gIds, enterDirs, exitDirs);
                    vm.Phases.Add(phaseVM);
                }
            }
        }
        catch
        {
            // ignored
        }
    }

    private static List<DirectiveItem> ParsePhaseDirectives(JsonElement phaseEl, string propertyName)
    {
        var result = new List<DirectiveItem>();

        if (!phaseEl.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in arr.EnumerateArray())
        {
            var typeStr = item.TryGetProperty("type", out var t) && t.ValueKind != JsonValueKind.Null
                              ? t.GetString() ?? "Plot"
                              : "Plot";

            var type = typeStr switch
            {
                "Tone"                => DirectiveType.Tone,
                "TemporaryConstraint" => DirectiveType.TemporaryConstraint,
                "SceneChange"         => DirectiveType.SceneChange,
                _                     => DirectiveType.Plot
            };

            var content = item.TryGetProperty("content", out var c) && c.ValueKind != JsonValueKind.Null
                              ? c.GetString() ?? string.Empty
                              : string.Empty;

            var ttl = item.TryGetProperty("ttl", out var ttlEl) && ttlEl.ValueKind == JsonValueKind.Number
                          ? ttlEl.GetInt32()
                          : (int?)null;

            result.Add(new DirectiveItem(type, content, result.Count + 1, ttl));
        }

        return result;
    }

    private async Task LoadKnowledgeAsync()
    {
        if (projectID <= 0)
            return;

        KnowledgeGroups.Clear();

        var groups  = await knowledgeRepository.GetGroupsAsync(projectID);
        var entries = await knowledgeRepository.GetByProjectAsync(projectID);

        foreach (var group in groups)
        {
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


            if (attr.Scope == StateScope.Category && attr.CategoryID is not null)
            {
                var catVM = CharacterCategories.FirstOrDefault(c => c.ID == attr.CategoryID.Value);

                catVM?.StateAttributes.Add(attrVM);
            }
            else
            {
                StateAttributes.Add(attrVM);
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
                ID              = projectID,
                Name            = Name.Trim(),
                Description     = Description,
                OpeningMessage  = OpeningMessage,
                EmbeddingConfig = BuildEmbeddingConfig(),
                AuditConfig     = BuildAuditConfig(),
                MemoryConfig    = BuildMemoryConfig(),
                KnowledgeConfig = BuildKnowledgeConfig()
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
            Title     = Loc.Get("Knowledge.Entry.New"),
            Content   = string.Empty,
            Tags      = [],
            GroupID = group?.ID,
            Active = true
        };

        var created = await knowledgeRepository.CreateAsync(entry);

        var entryVM = new KnowledgeEntryEditViewModel
        {
            ID           = created.ID,
            Title        = created.Title,
            Content      = created.Content,
            Tags         = string.Empty,
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
            var tags = entry.Tags
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var model = new KnowledgeEntry
            {
                ID        = entry.ID,
                ProjectID = projectID,
                Title     = entry.Title,
                Content   = entry.Content,
                Tags      = tags,
                GroupID   = entry.GroupID,
                Active    = entry.Active
            };

            await knowledgeRepository.UpdateAsync(model);
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
                CategoryID  = attribute.Scope == StateScope.Category ? attribute.CategoryID : null,
                ValueType   = attribute.ValueType,
                Driver      = attribute.Driver,
                Config      = attribute.BuildConfig()
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
    }

    [RelayCommand]
    private void AddPhase(StateAttributeEditViewModel? attribute)
    {
        if (attribute is null)
            return;

        var phase = new PhaseEditViewModel { IsEditing = true };
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
    private void AddPhaseKnowledge((PhaseEditViewModel phase, KnowledgeSelectionItem item) args)
    {
        args.phase.AddLinkedItem(args.item);
    }

    [RelayCommand]
    private void RemovePhaseKnowledge((PhaseEditViewModel phase, KnowledgeSelectionItem item) args)
    {
        args.phase.RemoveLinkedItem(args.item);
    }

    [RelayCommand]
    private async Task TestEmbeddingConnectionAsync()
    {
        Embedding.IsTestingConnection = true;
        Embedding.ConnectionSuccess   = null;
        Embedding.ConnectionMessage   = Loc.Get("Settings.TestingConnection");

        try
        {
            await connectionTester.TestEmbeddingAsync(Embedding.Provider, Embedding.Endpoint, Embedding.APIKey, Embedding.ModelName);

            Embedding.ConnectionSuccess = true;
            Embedding.ConnectionMessage = Loc.Get("Settings.ConnectionSuccess", Embedding.ModelName);
        }
        catch (Exception ex)
        {
            Embedding.ConnectionSuccess = false;
            Embedding.ConnectionMessage = Loc.Get("Settings.ConnectionFailed", ex.Message);
        }
        finally
        {
            Embedding.IsTestingConnection = false;
        }
    }
}
