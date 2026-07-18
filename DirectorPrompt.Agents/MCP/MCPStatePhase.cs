using System.ComponentModel;
using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Agents.MCP;

public sealed class MCPStatePhase
{
    [Description("阶段备注")]
    public string Name { get; set; } = string.Empty;

    [Description("条件表达式，{val} 代表当前状态属性值")]
    public string Expression { get; set; } = string.Empty;

    [Description("阶段命中后解锁的禁用知识条目 ID")]
    public long[] KnowledgeEntryIDs { get; set; } = [];

    [Description("阶段命中后解锁的禁用知识分组 ID")]
    public long[] KnowledgeGroupIDs { get; set; } = [];

    [Description("进入时执行的指令")]
    public List<DirectiveConfig> EnterDirectives { get; set; } = [];

    [Description("退出时执行的指令")]
    public List<DirectiveConfig> ExitDirectives { get; set; } = [];
}
