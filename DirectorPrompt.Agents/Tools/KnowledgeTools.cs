using System.Text.Json;
using DirectorPrompt.Agents.Retrieval;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Tools;

public sealed class KnowledgeTools
(
    KnowledgeRetrievalService retrievalService
)
{
    public IList<AIFunction> Create(ToolExecutionContext context) =>
    [
        AIFunctionFactory.Create
        (
            (string query) => QueryKnowledgeAsync(context, query),
            "query_knowledge",
            """
            语义检索知识条目
            query: 自包含的检索内容, 明确写出相关人物、地点、事件或规则, 不使用指代词
            返回条目 ID、原文、命中来源和语义相似度, 结果已由系统完成筛选
            """
        )
    ];

    private async Task<string> QueryKnowledgeAsync(ToolExecutionContext context, string query)
    {
        Log.Information("工具调用: query_knowledge(query={Query})", query);

        if (string.IsNullOrWhiteSpace(query))
            return JsonSerializer.Serialize(new { error = "检索内容不能为空" });

        var results = await retrievalService.SearchAsync(context, query);
        var response = results.Select
        (r =>
             new
             {
                 id                 = r.ID,
                 remarks            = r.Remarks,
                 content            = r.Content,
                 keywords           = r.Keywords,
                 matchedSource      = r.MatchedSource,
                 semanticSimilarity = Math.Round(r.SemanticSimilarity, 4)
             }
        );

        Log.Information("工具调用完成: query_knowledge, 返回条目数={Count}", results.Count);

        return JsonSerializer.Serialize(response);
    }
}
