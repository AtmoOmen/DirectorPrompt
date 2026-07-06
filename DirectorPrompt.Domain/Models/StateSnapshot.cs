namespace DirectorPrompt.Domain.Models;

public record StateSnapshot
{
    public long ID { get; init; }

    public long ProjectID { get; init; }

    public long SessionID { get; init; }

    public long RoundID { get; init; }

    public string GlobalState { get; init; } = "{}";

    public string CharacterState { get; init; } = "{}";

    public string ActiveDirectives { get; init; } = "{}";

    public long CurrentSceneID { get; init; }

    public string SceneCharacters { get; init; } = "[]";

    public DateTime CreatedAt { get; init; }
}
