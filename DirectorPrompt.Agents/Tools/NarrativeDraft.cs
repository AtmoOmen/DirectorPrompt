using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;

namespace DirectorPrompt.Agents.Tools;

public sealed class NarrativeDraft
(
    Action<string> onChanged
)
{
    private readonly Dictionary<string, string> intents = new(StringComparer.Ordinal);

    public string Text { get; private set; } = string.Empty;

    public int Revision { get; private set; }

    public bool IsSubmitted { get; private set; }

    public IReadOnlyDictionary<string, string> Intents => intents;

    public IReadOnlyList<AIFunction> CreateTools() =>
    [
        AIFunctionFactory.Create(Read,      "read_story", "查看仅包含本轮新增正文的草稿，返回带行号的文本、版本、总行数与叙事意图，不返回此前轮次的故事。行号仅用于定位，不属于正文。"),
        AIFunctionFactory.Create(Append,    "append_story", "向本轮草稿追加当前局部叙事单元的一段或多段新增 Markdown 正文，段落之间自动插入空行。首次直接承接历史结尾创作新内容，不复制或恢复上一轮故事；后续依据本轮草稿逐步续写。返回新版本与本轮计数。"),
        AIFunctionFactory.Create(Write,     "write_story", "需要全面重构时重写本轮新增 Markdown 正文，不是重写或合并整篇历史故事。先读取当前草稿，再决定重构；不得将此前轮次的正文复制进来。初次创作优先使用 append_story 分段推进。"),
        AIFunctionFactory.Create(Edit,      "edit_story", "精确替换草稿中唯一匹配的原文。可替换多段；replacement 为空时删除匹配内容。匹配不唯一时需要提供更多上下文。"),
        AIFunctionFactory.Create(Count,     "count_story", "统计本轮草稿的 Unicode 字符数、非空白字符数、段落数与行数，不改变正文。"),
        AIFunctionFactory.Create(SetIntent, "set_narrative_intent", "按稳定键添加或更新叙事意图，供后处理参考。用于说明伏笔、人物动机、记忆线索或状态变化依据；不直接修改状态。content 为空时移除。"),
        AIFunctionFactory.Create
        (
            Submit,
            "submit_story",
            "提交当前版本的非空草稿，结束本轮创作。必须已在最后一次正文修改后通过 read_story 读取复核，并收到结果；有字数要求时同时核对计数。仅在新的调用轮单独调用本工具，不与写入、读取或修改一起预先调用。只有已提交的工具草稿会作为最终故事，提交后不可修改。"
        )
    ];

    private string Read
    (
        [Description("起始行，包含该行，从 1 开始。默认查看全文。")]
        int startLine = 1,
        [Description("读取行数；0 表示读取至文末。")] int lineCount = 0
    )
    {
        var lines = Text.Split('\n');

        if (startLine < 1 || startLine > lines.Length || lineCount < 0)
            throw new ArgumentException("行范围无效，请根据 totalLines 指定范围。");

        var selected = lines.Skip(startLine - 1);

        if (lineCount > 0)
            selected = selected.Take(lineCount);

        return ToolResult.Data
        (
            new
            {
                revision   = Revision,
                totalLines = lines.Length,
                text       = string.Join('\n', selected.Select((line, index) => $"{startLine + index}: {line}")),
                intents,
                submitted = IsSubmitted
            }
        );
    }

    private string Append
    (
        [Description("要追加的一个或多个非空 Markdown 段落，按呈现顺序排列。")]
        string[] paragraphs,
        [Description("已读取或上次编辑返回的草稿版本，空草稿为 0。")]
        int expectedRevision
    )
    {
        CheckRevision(expectedRevision);

        if (paragraphs.Length == 0 || paragraphs.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("段落不能为空。");

        var appended = string.Join("\n\n", paragraphs.Select(paragraph => paragraph.Trim('\r', '\n')));
        return Change
        (
            string.IsNullOrEmpty(Text) ?
                appended :
                $"{Text.TrimEnd('\r', '\n')}\n\n{appended}"
        );
    }

    private string Write
    (
        string markdown,
        int    expectedRevision
    )
    {
        CheckRevision(expectedRevision);

        return string.IsNullOrWhiteSpace(markdown) ?
                   throw new ArgumentException("正文不能为空。") :
                   Change(markdown);
    }

    private string Edit
    (
        string oldText,
        string replacement,
        int    expectedRevision
    )
    {
        CheckRevision(expectedRevision);

        if (string.IsNullOrEmpty(oldText))
            throw new ArgumentException("需要提供非空的待替换原文。");

        oldText = oldText.ReplaceLineEndings("\n");
        var position = Text.IndexOf(oldText, StringComparison.Ordinal);

        if (position < 0)
            throw new ArgumentException("没有找到原文，请先调用 read_story 获取当前文本。");

        return Text.IndexOf(oldText, position + 1, StringComparison.Ordinal) >= 0 ?
                   throw new ArgumentException("原文存在多处匹配，请提供包含更多上下文的原文。") :
                   Change(string.Concat(Text.AsSpan(0, position), replacement, Text.AsSpan(position + oldText.Length)));
    }

    private string Count()
    {
        var characters              = 0;
        var nonWhitespaceCharacters = 0;

        foreach (var rune in Text.EnumerateRunes())
        {
            characters++;

            if (!Rune.IsWhiteSpace(rune))
                nonWhitespaceCharacters++;
        }

        return ToolResult.Data
        (
            new
            {
                revision = Revision,
                characters,
                nonWhitespaceCharacters,
                paragraphs = Text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Count(paragraph => !string.IsNullOrWhiteSpace(paragraph)),
                lines = Text.Length == 0 ?
                            0 :
                            Text.Count(character => character == '\n') + 1,
                submitted = IsSubmitted
            }
        );
    }

    private string SetIntent
    (
        string key,
        string content
    )
    {
        CheckRevision(Revision);

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("意图键不能为空。");

        if (string.IsNullOrWhiteSpace(content))
            intents.Remove(key);
        else
            intents[key] = content;

        Revision++;
        return ToolResult.Data(new { revision = Revision, intents });
    }

    private string Submit
    (
        int expectedRevision
    )
    {
        CheckRevision(expectedRevision);

        if (string.IsNullOrWhiteSpace(Text))
            throw new ArgumentException("无法提交空草稿，请先通过写作工具撰写正文。");

        IsSubmitted = true;
        return Count();
    }

    private void CheckRevision
    (
        int expectedRevision
    )
    {
        if (IsSubmitted)
            throw new InvalidOperationException("故事已经提交，不可继续修改。");

        if (expectedRevision != Revision)
            throw new ArgumentException($"草稿版本不一致：当前版本为 {Revision}，请重新查看草稿。");
    }

    private string Change
    (
        string text
    )
    {
        Text = text.ReplaceLineEndings("\n");
        Revision++;
        onChanged(Text);
        return Count();
    }
}
