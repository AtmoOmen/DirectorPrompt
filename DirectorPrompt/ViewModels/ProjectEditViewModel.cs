using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly IModelConnectionTester connectionTester;

    private long projectID;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(TitleText))]
    private string name = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private string openingMessage = string.Empty;

    [ObservableProperty]
    private bool isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string validationMessage = string.Empty;

    public ObservableCollection<KnowledgeGroupEditViewModel> KnowledgeGroups { get; } = [];

    public ObservableCollection<StateAttributeEditViewModel> StateAttributes { get; } = [];

    public EmbeddingSettingViewModel Embedding { get; } = new();

    public AuditSettingViewModel Audit { get; } = new();

    public MemorySettingViewModel Memory { get; } = new();

    public KnowledgeSettingViewModel Knowledge { get; } = new();

    public bool IsEditing => projectID > 0;

    public string TitleText => IsEditing ?
                                   Loc.Get("Project.EditTitle") :
                                   Loc.Get("Project.NewTitle");

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    public bool SaveSuccess { get; private set; }

    public long SavedProjectID { get; private set; }

    public ProjectEditViewModel
    (
        IProjectRepository     projectRepository,
        IKnowledgeRepository   knowledgeRepository,
        IStateRepository       stateRepository,
        IModelConnectionTester connectionTester
    )
    {
        this.projectRepository   = projectRepository;
        this.knowledgeRepository = knowledgeRepository;
        this.stateRepository     = stateRepository;
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

    private static void ParseStateConfig(StateAttributeEditViewModel vm, string json)
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
        }
        catch
        {
            // ignored
        }
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

        var attributes = await stateRepository.GetAttributesAsync(projectID);
        var values     = await stateRepository.GetAllStateValuesAsync(projectID, 0);

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
                CurrentValue = value?.Value ?? string.Empty
            };

            ParseStateConfig(attrVM, attr.Config);
            StateAttributes.Add(attrVM);
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

            if (projectID > 0)
            {
                await projectRepository.UpdateAsync(project);
                SavedProjectID = projectID;
                SaveSuccess    = true;
            }
            else
            {
                var created = await projectRepository.CreateAsync(project);
                SavedProjectID = created.ID;
                projectID      = created.ID;
                SaveSuccess    = true;

                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(TitleText));
            }

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
                Scope       = StateScope.Global,
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
            return;
        }

        try
        {
            await stateRepository.DeleteAttributeAsync(attribute.ID);
            StateAttributes.Remove(attribute);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除状态属性失败");
            ValidationMessage = Loc.Get("Common.DeleteFailed", ex.Message);
        }
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
