namespace DirectorPrompt.Agents.Retrieval;

public sealed record MemoryRetrievalResult
(
    long     ID,
    string   Content,
    string[] Tags,
    long     SceneID,
    string   MatchedSource,
    float    SemanticSimilarity,
    double   RecencyWeight,
    double   FinalScore
);
