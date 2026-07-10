namespace DirectorPrompt.Agents.Prompts;

public static class SceneSummaryPrompt
{
    public const string SYSTEM =
        """
        请将以下场景内的对话历史压缩为简短摘要 (约 300-500 字), 包含关键事件、人物状态变化和情节发展。只输出摘要内容, 不要添加额外格式。
        """;
}
