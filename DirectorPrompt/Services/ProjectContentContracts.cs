using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Services;

public enum ProjectContentAction
{
    Create,
    Update,
    Delete
}

public enum ProjectImportFormat
{
    DirectorPromptPackage,
    SillyTavernCharacterCard
}

public sealed class ProjectBlueprint
{
    public List<KnowledgeGroupDefinition> KnowledgeGroups { get; set; } = [];

    public List<CharacterCategoryDefinition> CharacterCategories { get; set; } = [];

    public List<StateAttributeDefinition> StateAttributes { get; set; } = [];
}

public sealed class KnowledgeGroupDefinition
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool Active { get; set; } = true;

    public List<KnowledgeEntryDefinition> Entries { get; set; } = [];
}

public sealed class KnowledgeEntryDefinition
{
    public string Key { get; set; } = string.Empty;

    public string Remarks { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public List<string> Keywords { get; set; } = [];

    public bool Active { get; set; } = true;
}

public sealed class CharacterCategoryDefinition
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<string> ParentCategoryKeys { get; set; } = [];

    public List<StateAttributeDefinition> StateAttributes { get; set; } = [];
}

public sealed class StateAttributeDefinition
{
    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public StateScope Scope { get; set; } = StateScope.Global;

    public long? CategoryID { get; set; }

    public StateValueType ValueType { get; set; } = StateValueType.Numeric;

    public Driver Driver { get; set; } = Driver.Narrative;

    public NumericStateDefinition? Numeric { get; set; }

    public EnumStateDefinition? Enumeration { get; set; }

    public List<PhaseDefinition> Phases { get; set; } = [];
}

public sealed class NumericStateDefinition
{
    public float? Min { get; set; }

    public float? Max { get; set; }

    public string? Unit { get; set; }

    public string? ChangeRules { get; set; }
}

public sealed class EnumStateDefinition
{
    public List<string> Options { get; set; } = [];

    public SystemTrigger Trigger { get; set; } = SystemTrigger.SceneChange;

    public List<EnumTransitionConfig> Transitions { get; set; } = [];
}

public sealed class PhaseDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Expression { get; set; } = string.Empty;

    public List<long> KnowledgeEntryIDs { get; set; } = [];

    public List<long> KnowledgeGroupIDs { get; set; } = [];

    public List<string> KnowledgeEntryKeys { get; set; } = [];

    public List<string> KnowledgeGroupKeys { get; set; } = [];

    public List<DirectiveConfig> EnterDirectives { get; set; } = [];

    public List<DirectiveConfig> ExitDirectives { get; set; } = [];
}

public sealed record ProjectContentSnapshot
(
    Project                                  Project,
    IReadOnlyList<ProjectKnowledgeGroup>     KnowledgeGroups,
    IReadOnlyList<KnowledgeEntry>            UngroupedKnowledgeEntries,
    IReadOnlyList<CharacterCategory>         CharacterCategories,
    IReadOnlyList<ProjectStateAttribute>     StateAttributes
);

public sealed record ProjectKnowledgeGroup
(
    KnowledgeGroup                   Group,
    IReadOnlyList<KnowledgeEntry>    Entries
);

public sealed record ProjectStateAttribute
(
    long                 ID,
    long                 ProjectID,
    string               Name,
    string               DisplayName,
    StateScope           Scope,
    long?                CategoryID,
    StateValueType       ValueType,
    Driver               Driver,
    StateAttributeConfig Configuration
);

public sealed record ProjectBlueprintResult
(
    Project                      Project,
    IReadOnlyDictionary<string, long> CategoryIDs,
    IReadOnlyDictionary<string, long> GroupIDs,
    IReadOnlyDictionary<string, long> EntryIDs,
    string                       IndexStatus
);

public sealed record ProjectDeleteSummary
(
    int Projects,
    int KnowledgeGroups,
    int KnowledgeEntries,
    int CharacterCategories,
    int StateAttributes,
    int Transitions,
    int PhaseReferences
);

public sealed record ProjectContentChange
(
    long    ProjectID,
    bool    IsDeleted
);
