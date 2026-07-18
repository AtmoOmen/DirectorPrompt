using System.ComponentModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.Agents.MCP;

public sealed class MCPEnumStateTransition
{
    [Description("当前枚举选项，必须存在于状态属性的选项列表中")]
    public string Option { get; set; } = string.Empty;

    [Description("转移方式：Random 按权重随机选择，Expression 根据表达式选择")]
    public EnumTransitionMethod Method { get; set; } = EnumTransitionMethod.Random;

    [Description("随机转移权重，仅 Method 为 Random 时使用")]
    public float Weight { get; set; } = 1f;

    [Description("表达式中引用的数值状态属性名称，仅 Method 为 Expression 时使用")]
    public string? AttributeName { get; set; }

    [Description("枚举转移表达式，仅 Method 为 Expression 时使用")]
    public string? Expression { get; set; }

    [Description("表达式转移执行方式：Always 每次触发都执行，Once 仅执行一次")]
    public EnumSwitchMode SwitchMode { get; set; } = EnumSwitchMode.Always;
}
