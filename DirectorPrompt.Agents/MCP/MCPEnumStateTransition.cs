using System.ComponentModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents.MCP;

public sealed class MCPEnumStateTransition
{
    [Description("目标枚举选项，必须存在于属性选项中")]
    public string Option { get; set; } = string.Empty;

    [Description("叙事驱动时，该选项的变更指引")]
    public string? ChangeRules { get; set; }

    [Description("系统驱动时的转移方式：Random 按权重随机，Expression 按条件匹配")]
    public EnumTransitionMethod Method { get; set; } = EnumTransitionMethod.Random;

    [Description("系统驱动时的转移权重；必须为大于或等于 0 的有限数，多个表达式命中时优先较大值")]
    public float Weight { get; set; } = 1f;

    [Description("系统驱动表达式引用的数值属性标识；{val} 代表该属性当前值")]
    public string? AttributeName { get; set; }

    [Description("系统驱动转移条件表达式，{val} 代表 AttributeName 对应属性值")]
    public string? Expression { get; set; }

    [Description("系统驱动表达式转移方式：Always 每次触发，Once 仅首次命中")]
    public EnumSwitchMode SwitchMode { get; set; } = EnumSwitchMode.Always;
}
