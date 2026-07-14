namespace DirectorPrompt.Domain.Models;

public sealed record SidebarSnapshot
(
    string?                         SceneLabel,
    IReadOnlyList<SidebarStateItem> StateItems,
    IReadOnlyList<ActiveDirective>  ActiveDirectives
);
