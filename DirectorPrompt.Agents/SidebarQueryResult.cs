using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Agents;

public sealed record SidebarQueryResult
(
    SidebarQueryResult.StatePanelData       StatePanel,
    SidebarQueryResult.DirectivesPanelData  DirectivesPanel,
    SidebarQueryResult.CharacterPanelData   CharacterPanel,
    SidebarQueryResult.MemoryPanelData      MemoryPanel
)
{
    public sealed record StatePanelData
    (
        string                                SceneLabel,
        IReadOnlyList<StatePanelItem>         Items
    );

    public sealed record StatePanelItem
    (
        string Name,
        string Value,
        string Scope
    );

    public sealed record DirectivesPanelData
    (
        IReadOnlyList<DirectivesPanelItem> Items
    );

    public sealed record DirectivesPanelItem
    (
        DirectiveType Type,
        string        Content,
        bool          HasTTL,
        int?          TTL
    );

    public sealed record CharacterPanelData
    (
        IReadOnlyList<CharacterPanelGroup> Groups
    );

    public sealed record CharacterPanelGroup
    (
        string?                                CategoryName,
        IReadOnlyList<CharacterPanelItem>     Items
    );

    public sealed record CharacterPanelItem
    (
        long                                         ID,
        string                                       Name,
        string                                       Description,
        string                                       Categories,
        IReadOnlyList<CharacterStateValueItem>       StateValues,
        IReadOnlyList<CharacterRelationItem>         Relations
    );

    public sealed record CharacterStateValueItem
    (
        string Name,
        string Value
    );

    public sealed record CharacterRelationItem
    (
        string Target,
        string Type,
        string Description,
        string Direction
    );

    public sealed record MemoryPanelData
    (
        IReadOnlyList<MemoryPanelGroup> Groups
    );

    public sealed record MemoryPanelGroup
    (
        string                                SceneLabel,
        IReadOnlyList<MemoryPanelItem>        Items
    );

    public sealed record MemoryPanelItem
    (
        long   ID,
        string Content,
        string TagsDisplay,
        string SceneLabel,
        long   TimelinePos,
        string RelatedCharacters,
        bool   HasRelatedCharacters,
        string UpdatedAtDisplay
    );
}
