using System.ComponentModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents.MCP;

public sealed class MCPEnumStateTransition
{
    [Description("规则备注")]
    public string Remarks { get; set; } = string.Empty;

    [Description("目标枚举选项，必须存在于属性选项中")]
    public string Option { get; set; } = string.Empty;

    [Description("叙事驱动时，该选项的变更指引")]
    public string? ChangeRules { get; set; }

    [Description("系统驱动时的转移权重；必须为大于或等于 0 的有限数，多个表达式命中时优先较大值")]
    public float Weight { get; set; } = 1f;

    [Description("该选项规则的触发时机")]
    public SystemTrigger? Trigger { get; set; }

    [Description("普通条件列表；提供后不再要求手写 expression")]
    public List<MCPStateRuleCondition> Conditions { get; set; } = [];

    [Description("多个普通条件的组合方式")]
    public StateRuleConditionMatch ConditionMatch { get; set; } = StateRuleConditionMatch.All;

    [Description("重复生效方式")]
    public StateRuleRepeatPolicy? RepeatPolicy { get; set; }

    [Description("优先级；只在最高优先级候选中按权重选择")]
    public int Priority { get; set; }
}
