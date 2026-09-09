using System.ComponentModel;
using System.Text.Json.Serialization;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Domain.Models;

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
    [Description("知识分组列表")]
    public List<KnowledgeGroupDefinition> KnowledgeGroups { get; set; } = [];

    [Description("人物分类列表")]
    public List<CharacterCategoryDefinition> CharacterCategories { get; set; } = [];

    [Description("全局状态属性列表")]
    public List<StateAttributeDefinition> StateAttributes { get; set; } = [];
}

public sealed class KnowledgeGroupDefinition
{
    [Description("分组唯一标识, 同一次蓝图内引用分组时使用该值")]
    public string Key { get; set; } = string.Empty;

    [Description("分组名称")]
    public string Name { get; set; } = string.Empty;

    [Description("分组描述")]
    public string? Description { get; set; }

    [Description("是否启用")]
    public bool Active { get; set; } = true;

    [Description("分组内的知识条目")]
    public List<KnowledgeEntryDefinition> Entries { get; set; } = [];
}

public sealed class KnowledgeEntryDefinition
{
    [Description("条目唯一标识, 同一次蓝图内引用条目时使用该值")]
    public string Key { get; set; } = string.Empty;

    [Description("条目备注")]
    public string Remarks { get; set; } = string.Empty;

    [Description("条目内容")]
    public string Content { get; set; } = string.Empty;

    [Description("匹配关键词")]
    public List<string> Keywords { get; set; } = [];

    [Description("是否启用")]
    public bool Active { get; set; } = true;
}

public sealed class CharacterCategoryDefinition
{
    [Description("分类唯一标识, 同一次蓝图内引用分类时使用该值")]
    public string Key { get; set; } = string.Empty;

    [Description("分类名称")]
    public string Name { get; set; } = string.Empty;

    [Description("分类描述")]
    public string? Description { get; set; }

    [Description("继承状态属性的上级分类 Key 列表")]
    public List<string> ParentCategoryKeys { get; set; } = [];

    [Description("分类下的状态属性")]
    public List<StateAttributeDefinition> StateAttributes { get; set; } = [];
}

public sealed class StateAttributeDefinition
{
    [Description("属性标识, 供表达式与工具调用引用")]
    public string Name { get; set; } = string.Empty;

    [Description("显示名")]
    public string DisplayName { get; set; } = string.Empty;

    [Description("作用域")]
    public StateScope Scope { get; set; } = StateScope.Global;

    [JsonIgnore]
    public long? CategoryID { get; set; }

    [Description("值类型")]
    public StateValueType ValueType { get; set; } = StateValueType.Numeric;

    [Description("驱动方式")]
    public Driver Driver { get; set; } = Driver.Narrative;

    [Description("数值属性配置")]
    public NumericStateDefinition? Numeric { get; set; }

    [Description("枚举属性配置")]
    public EnumStateDefinition? Enumeration { get; set; }

    [Description("文本属性配置")]
    public TextStateDefinition? Text { get; set; }

    [Description("阶段规则")]
    public List<PhaseDefinition> Phases { get; set; } = [];
}

public sealed class NumericStateDefinition
{
    [Description("最小值")]
    public float? Min { get; set; }

    [Description("最大值")]
    public float? Max { get; set; }

    [Description("初始值")]
    public float? Initial { get; set; }

    [Description("单位")]
    public string? Unit { get; set; }

    [Description("变更指引")]
    public string? ChangeRules { get; set; }

    [Description("系统驱动时的数值变更条件")]
    public List<NumericStateChangeRuleConfig> Changes { get; set; } = [];
}

public sealed class EnumStateDefinition
{
    [Description("枚举选项, 至少一项")]
    public List<string> Options { get; set; } = [];

    [Description("系统驱动时的触发时机")]
    public SystemTrigger Trigger { get; set; } = SystemTrigger.SceneChange;

    [Description("各枚举选项的配置")]
    public List<EnumTransitionConfig> Transitions { get; set; } = [];
}

public sealed class PhaseDefinition
{
    [Description("阶段备注")]
    public string Name { get; set; } = string.Empty;

    [Description("条件表达式, {val} 代表当前状态属性值")]
    public string Expression { get; set; } = string.Empty;

    [JsonIgnore]
    public List<long> KnowledgeEntryIDs { get; set; } = [];

    [JsonIgnore]
    public List<long> KnowledgeGroupIDs { get; set; } = [];

    [Description("阶段命中后解锁的禁用知识条目 Key 列表")]
    public List<string> KnowledgeEntryKeys { get; set; } = [];

    [Description("阶段命中后解锁的禁用知识分组 Key 列表")]
    public List<string> KnowledgeGroupKeys { get; set; } = [];

    [Description("进入时执行的指令")]
    public List<DirectiveConfig> EnterDirectives { get; set; } = [];

    [Description("退出时执行的指令")]
    public List<DirectiveConfig> ExitDirectives { get; set; } = [];
}

public sealed record ProjectContentSnapshot
(
    Project                              Project,
    IReadOnlyList<ProjectKnowledgeGroup> KnowledgeGroups,
    IReadOnlyList<KnowledgeEntry>        UngroupedKnowledgeEntries,
    IReadOnlyList<CharacterCategory>     CharacterCategories,
    IReadOnlyList<ProjectStateAttribute> StateAttributes
);

public sealed record ProjectKnowledgeGroup
(
    KnowledgeGroup                Group,
    IReadOnlyList<KnowledgeEntry> Entries
);

public sealed record ProjectStateAttribute
(
    long       ID,
    long       ProjectID,
    string     Name,
    string     DisplayName,
    StateScope Scope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    long? CategoryID,
    StateValueType       ValueType,
    Driver               Driver,
    StateAttributeConfig Configuration
);

public sealed record ProjectBlueprintResult
(
    Project                           Project,
    IReadOnlyDictionary<string, long> CategoryIDs,
    IReadOnlyDictionary<string, long> GroupIDs,
    IReadOnlyDictionary<string, long> EntryIDs
);

public sealed record ProjectExportResult
(
    long   ProjectID,
    string FilePath
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
    long ProjectID,
    bool IsDeleted
);
