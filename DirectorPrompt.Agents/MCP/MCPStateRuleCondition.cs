using System.ComponentModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents.MCP;

public sealed class MCPStateRuleCondition
{
    [Description("条件来源")]
    public StateRuleConditionSource Source { get; set; } = StateRuleConditionSource.CurrentValue;

    [Description("状态属性标识；全局状态填写状态名，当前人物状态填写 角色分类名.状态名")]
    public string? StateName { get; set; }

    [Description("比较方式；Expression 可使用 {val}、{global.状态名}、{角色分类名.状态名}")]
    public StateRuleComparison Comparison { get; set; } = StateRuleComparison.Equals;

    [Description("比较值或表达式；当前输入内容按实际文本填写，不添加、删除或解释前缀")]
    public string ExpectedValue { get; set; } = string.Empty;
}
