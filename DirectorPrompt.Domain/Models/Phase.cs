namespace DirectorPrompt.Domain.Models;

public record Phase
{
    public string Name { get; init; } = string.Empty;

    public string Expression { get; init; } = string.Empty;

    public IReadOnlyList<long> KnowledgeIDs { get; init; } = [];

    public IReadOnlyList<long> KnowledgeGroupIDs { get; init; } = [];

    public IReadOnlyList<DirectiveConfig> EnterDirectives { get; init; } = [];

    public IReadOnlyList<DirectiveConfig> ExitDirectives { get; init; } = [];
}
