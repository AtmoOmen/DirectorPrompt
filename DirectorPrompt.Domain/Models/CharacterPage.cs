namespace DirectorPrompt.Domain.Models;

public sealed record CharacterPage
(
    IReadOnlyList<Character> Items,
    long?                    NextID
);
