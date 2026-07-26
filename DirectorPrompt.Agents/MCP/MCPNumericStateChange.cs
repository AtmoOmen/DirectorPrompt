using System.ComponentModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents.MCP;

public sealed class MCPNumericStateChange
{
    [Description("规则备注")]
    public string Remarks { get; set; } = string.Empty;

    [Description("触发时机")]
    public SystemTrigger Trigger { get; set; } = SystemTrigger.RoundEnd;

    [Description("普通条件列表；提供后不再要求手写 expression")]
    public List<MCPStateRuleCondition> Conditions { get; set; } = [];

    [Description("多个普通条件的组合方式")]
    public StateRuleConditionMatch ConditionMatch { get; set; } = StateRuleConditionMatch.All;

    [Description("数值操作；Add 增加、Set 设为、Multiply 乘以、Expression 表达式计算")]
    public NumericStateOperation Operation { get; set; } = NumericStateOperation.Add;

    [Description("数值操作使用的数值或表达式；可使用 {val}、{global.状态名}、{角色分类名.状态名}")]
    public string ValueExpression { get; set; } = "1";

    [Description("重复生效方式")]
    public StateRuleRepeatPolicy? RepeatPolicy { get; set; }

    [Description("优先级")]
    public int Priority { get; set; }
}
