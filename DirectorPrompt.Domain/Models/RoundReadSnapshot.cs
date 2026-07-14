namespace DirectorPrompt.Domain.Models;

public sealed record RoundReadSnapshot
{
    public Scene? Scene { get; init; }

    public IReadOnlyList<StateAttribute> GlobalAttributes { get; init; } = [];

    public IReadOnlyList<StateValue> GlobalValues { get; init; } = [];

    public IReadOnlyList<ActiveDirective> ActiveDirectives { get; init; } = [];

    public IReadOnlyList<Character> SceneCharacters { get; init; } = [];

    public IReadOnlyList<StateAttribute> CharacterAttributes { get; init; } = [];

    public IReadOnlyList<CharacterStateValue> CharacterValues { get; init; } = [];

    public IReadOnlyList<CharacterRelation> CharacterRelations { get; init; } = [];
}
