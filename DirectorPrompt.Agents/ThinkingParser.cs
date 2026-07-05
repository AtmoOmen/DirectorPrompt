using System.Text;
using System.Text.RegularExpressions;

namespace DirectorPrompt.Agents;

/// <summary>
/// 解析 AI 响应中的 Thinking 块, 支持两种来源:
/// 1. Microsoft.Extensions.AI 的 TextReasoningContent (API 原生推理)
/// 2. 内联 &lt;think&gt;...&lt;/think&gt; 标签 (DeepSeek 等模型的 OpenAI 兼容接口)
/// </summary>
public static class ThinkingParser
{
    private static readonly Regex ThinkTagRegex = new(
        @"<think>([\s\S]*?)</think>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    /// <summary>
    /// 从原始文本中提取内联 think 标签, 返回 (thinking, narrative)
    /// </summary>
    public static (string thinking, string narrative) ParseInline(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return (string.Empty, raw);

        var thinkingBuilder = new StringBuilder();
        var narrativeBuilder = new StringBuilder();
        var lastIndex = 0;

        foreach (Match match in ThinkTagRegex.Matches(raw))
        {
            if (match.Index > lastIndex)
                narrativeBuilder.Append(raw.AsSpan(lastIndex, match.Index - lastIndex));

            thinkingBuilder.AppendLine(match.Groups[1].Value.Trim());
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < raw.Length)
            narrativeBuilder.Append(raw.AsSpan(lastIndex));

        return (thinkingBuilder.ToString().TrimEnd(), narrativeBuilder.ToString().TrimStart());
    }

    /// <summary>
    /// 合并 API 原生推理内容和内联 think 标签内容
    /// </summary>
    public static (string thinking, string narrative) Merge(string apiReasoning, string rawText)
    {
        var (inlineThinking, narrative) = ParseInline(rawText);

        var thinking = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(apiReasoning))
        {
            thinking.AppendLine(apiReasoning.Trim());
        }

        if (!string.IsNullOrWhiteSpace(inlineThinking))
        {
            if (thinking.Length > 0)
                thinking.AppendLine();

            thinking.Append(inlineThinking);
        }

        return (thinking.ToString().TrimEnd(), narrative);
    }
}
