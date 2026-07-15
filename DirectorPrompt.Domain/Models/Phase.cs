using System.Text.Json.Serialization;

namespace DirectorPrompt.Domain.Models;

public record Phase
{
    public string Name { get; init; } = string.Empty;

    public string Expression { get; init; } = string.Empty;

    [JsonPropertyName("knowledgeIds")]
    public IReadOnlyList<long> KnowledgeIDs { get; init; } = [];

    [JsonPropertyName("knowledgeGroupIds")]
    public IReadOnlyList<long> KnowledgeGroupIDs { get; init; } = [];

    public IReadOnlyList<DirectiveConfig> EnterDirectives { get; init; } = [];

    public IReadOnlyList<DirectiveConfig> ExitDirectives { get; init; } = [];
}
