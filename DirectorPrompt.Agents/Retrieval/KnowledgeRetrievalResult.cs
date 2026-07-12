namespace DirectorPrompt.Agents.Retrieval;

public sealed record KnowledgeRetrievalResult
(
    long     ID,
    string   Remarks,
    string   Content,
    string[] Keywords,
    string   MatchedSource,
    float    SemanticSimilarity
);
