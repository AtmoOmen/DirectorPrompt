using System.ComponentModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents.MCP;

public sealed class MCPNumericStateChange
{
    [Description("规则备注")]
    public string Remarks { get; set; } = string.Empty;

    [Description("关联数值属性标识；留空时条件不关联其他属性")]
    public string? AttributeName { get; set; }

    [Description("生效条件，{val} 代表关联属性值；默认 true == true")]
    public string Expression { get; set; } = "true == true";

    [Description("数值变更式，{val} 代表当前属性值，例如 {val} += 10")]
    public string ChangeExpression { get; set; } = "{val} += 0";

    [Description("触发时机")]
    public SystemTrigger Trigger { get; set; } = SystemTrigger.RoundEnd;

    [Description("Always 每次满足时生效，Once 仅首次满足时生效")]
    public EnumSwitchMode SwitchMode { get; set; } = EnumSwitchMode.Always;
}
