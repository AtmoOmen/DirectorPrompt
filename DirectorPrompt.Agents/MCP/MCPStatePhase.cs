using System.ComponentModel;
using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Agents.MCP;

public sealed class MCPStatePhase
{
    [Description("阶段名称")]
    public string Name { get; set; } = string.Empty;

    [Description("进入该阶段的条件表达式")]
    public string Expression { get; set; } = string.Empty;

    [Description("进入阶段时启用的知识条目 ID 列表")]
    public long[] KnowledgeEntryIDs { get; set; } = [];

    [Description("进入阶段时启用的知识分组 ID 列表")]
    public long[] KnowledgeGroupIDs { get; set; } = [];

    [Description("进入阶段时执行的指令列表")]
    public List<DirectiveConfig> EnterDirectives { get; set; } = [];

    [Description("退出阶段时执行的指令列表")]
    public List<DirectiveConfig> ExitDirectives { get; set; } = [];
}
