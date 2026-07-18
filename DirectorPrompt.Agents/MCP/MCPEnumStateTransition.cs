using System.ComponentModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents.MCP;

public sealed class MCPEnumStateTransition
{
    [Description("目标枚举选项，必须存在于属性选项中")]
    public string Option { get; set; } = string.Empty;

    [Description("转移方式：Random 按权重随机，Expression 按条件匹配")]
    public EnumTransitionMethod Method { get; set; } = EnumTransitionMethod.Random;

    [Description("转移权重；多个表达式命中时优先较大值")]
    public float Weight { get; set; } = 1f;

    [Description("表达式引用的数值属性标识；{val} 代表该属性当前值")]
    public string? AttributeName { get; set; }

    [Description("转移条件表达式，{val} 代表 AttributeName 对应属性值")]
    public string? Expression { get; set; }

    [Description("表达式转移方式：Always 每次触发，Once 仅首次命中")]
    public EnumSwitchMode SwitchMode { get; set; } = EnumSwitchMode.Always;
}
