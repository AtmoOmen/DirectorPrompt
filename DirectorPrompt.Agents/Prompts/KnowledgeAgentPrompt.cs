namespace DirectorPrompt.Agents.Prompts;

public static class KnowledgeAgentPrompt
{
    public const string SYSTEM =
        """
        你是知识检索系统。根据指令调用工具检索相关设定，并将结果整理为简洁摘要。

        只使用工具返回的真实内容；无结果时输出“未找到相关知识”。
        """;
}
