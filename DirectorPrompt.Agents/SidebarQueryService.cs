using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Serilog;

namespace DirectorPrompt.Agents;

public sealed class SidebarQueryService
(
    ISceneRepository     sceneRepository,
    IStateRepository     stateRepository,
    IDirectiveRepository directiveRepository,
    ICharacterRepository characterRepository,
    IMemoryRepository    memoryRepository
)
{
    public async Task<SidebarQueryResult.StatePanelData> QueryStatePanelAsync
    (
        long projectID,
        long sessionID,
        CancellationToken token = default
    )
    {
        var scene = await sceneRepository.GetActiveSceneAsync(sessionID, token);

        var sceneLabel = scene?.TimeLabel ?? string.Empty;

        var values     = await stateRepository.GetAllStateValuesAsync(projectID, sessionID, token);
        var attributes = await stateRepository.GetAttributesAsync(projectID, null, token);

        var items = attributes
                    .Where(a => a.Scope == StateScope.Global)
                    .Select
                    (
                        attr =>
                        {
                            var value = values.FirstOrDefault(v => v.AttributeID == attr.ID);

                            return new SidebarQueryResult.StatePanelItem
                            (
                                attr.DisplayName,
                                value?.Value ?? "—",
                                attr.Scope.ToString()
                            );
                        }
                    )
                    .ToList();

        return new SidebarQueryResult.StatePanelData(sceneLabel, items);
    }

    public async Task<SidebarQueryResult.DirectivesPanelData> QueryDirectivesPanelAsync
    (
        long sessionID,
        CancellationToken token = default
    )
    {
        var directives = await directiveRepository.GetActiveAsync(sessionID, token);

        var items = directives
                    .Select
                    (
                        d => new SidebarQueryResult.DirectivesPanelItem
                        (
                            d.Type,
                            d.Content,
                            d.TTL.HasValue,
                            d.TTL
                        )
                    )
                    .ToList();

        return new SidebarQueryResult.DirectivesPanelData(items);
    }

    public async Task<SidebarQueryResult.CharacterPanelData> QueryCharacterPanelAsync
    (
        long projectID,
        long sessionID,
        CancellationToken token = default
    )
    {
        var characters    = await characterRepository.GetBySessionAsync(sessionID, token);
        var categories    = await characterRepository.GetCategoriesAsync(projectID, token);
        var categoryAttrs = await stateRepository.GetAttributesAsync(projectID, StateScope.Category, token);

        var charLookup     = characters.ToDictionary(c => c.ID);
        var categoryLookup = categories.ToDictionary(c => c.ID);

        var items = new List<(SidebarQueryResult.CharacterPanelItem Item, long[] CategoryIDs)>();

        foreach (var c in characters)
        {
            var categoriesDisplay = string.Join
            (
                ", ",
                categories.Where(cat => c.CategoryIDs.Contains(cat.ID)).Select(cat => cat.Name)
            );

            var stateValues = await characterRepository.GetCharacterStateValuesAsync(c.ID, token);

            var stateValueItems = stateValues
                                  .Select
                                  (
                                      sv =>
                                      {
                                          var attr = categoryAttrs.FirstOrDefault(a => a.ID == sv.AttributeID);

                                          return new SidebarQueryResult.CharacterStateValueItem
                                          (
                                              attr?.DisplayName ?? attr?.Name ?? sv.AttributeID.ToString(),
                                              sv.Value
                                          );
                                      }
                                  )
                                  .ToList();

            var relations = await characterRepository.GetRelationsByCharacterAsync(c.ID, token);

            var relationItems = relations
                                .Select
                                (
                                    r =>
                                    {
                                        var otherID   = r.SourceCharacterID == c.ID ? r.TargetCharacterID : r.SourceCharacterID;
                                        var otherName = charLookup.TryGetValue(otherID, out var other) ? other.Name : $"ID:{otherID}";
                                        var direction = r.SourceCharacterID == c.ID ? "→" : "←";

                                        return new SidebarQueryResult.CharacterRelationItem
                                        (
                                            otherName,
                                            r.RelationType,
                                            r.Description ?? string.Empty,
                                            direction
                                        );
                                    }
                                )
                                .ToList();

            var item = new SidebarQueryResult.CharacterPanelItem
            (
                c.ID,
                c.Name,
                c.Description,
                categoriesDisplay,
                stateValueItems,
                relationItems
            );

            items.Add((item, c.CategoryIDs));
        }

        var grouped = items
                      .SelectMany
                      (
                          it => it.CategoryIDs.Length > 0 ?
                                     it.CategoryIDs.Select(catID => (CatID: catID, it.Item)) :
                                     [(-1L, it.Item)]
                      )
                      .GroupBy(x => x.CatID)
                      .OrderBy(g => g.Key)
                      .ToList();

        var groups = new List<SidebarQueryResult.CharacterPanelGroup>();

        foreach (var grp in grouped)
        {
            var groupName = grp.Key >= 0 && categoryLookup.TryGetValue(grp.Key, out var cat) ?
                                cat.Name :
                                null;

            groups.Add(new SidebarQueryResult.CharacterPanelGroup(groupName, grp.Select(x => x.Item).ToList()));
        }

        return new SidebarQueryResult.CharacterPanelData(groups);
    }

    public async Task<SidebarQueryResult.MemoryPanelData> QueryMemoryPanelAsync
    (
        long sessionID,
        CancellationToken token = default
    )
    {
        var memories = new List<MemoryEntry>();

        long? beforeTimelinePosition = null;
        long? beforeID               = null;

        do
        {
            var page = await memoryRepository.GetPageAsync
                       (
                           new MemoryPageQuery
                           (
                               sessionID,
                               long.MaxValue,
                               beforeTimelinePosition,
                               beforeID,
                               PageSize: 200
                           ),
                           token
                       );

            memories.AddRange(page.Items);
            beforeTimelinePosition = page.NextTimelinePosition;
            beforeID               = page.NextID;
        }
        while (beforeTimelinePosition is not null && beforeID is not null);

        var scenes     = await sceneRepository.GetBySessionAsync(sessionID, token);
        var characters = await characterRepository.GetBySessionAsync(sessionID, token);

        var sceneLookup = scenes.ToDictionary(s => s.ID);
        var charLookup  = characters.ToDictionary(c => c.ID);

        var grouped = memories
                      .GroupBy(m => m.SceneID)
                      .Select
                      (
                          g =>
                          {
                              var scene = sceneLookup.GetValueOrDefault(g.Key);
                              var label = scene is not null ? scene.TimeLabel : $"ID:{g.Key}";

                              return new
                              {
                                  Label       = label,
                                  TimelinePos = scene?.TimelinePosition ?? 0,
                                  Items       = g
                              };
                          }
                      )
                      .OrderBy(x => x.TimelinePos)
                      .ToList();

        var groups = new List<SidebarQueryResult.MemoryPanelGroup>();

        foreach (var grp in grouped)
        {
            var memoryItems = grp.Items
                                 .Select
                                 (
                                     m =>
                                     {
                                         var charNames = m.RelatedCharacterIDs
                                                          .Where(charLookup.ContainsKey)
                                                          .Select(id => charLookup[id].Name)
                                                          .ToList();

                                         return new SidebarQueryResult.MemoryPanelItem
                                         (
                                             m.ID,
                                             m.Content,
                                             string.Join(", ", m.Tags),
                                             grp.Label,
                                             m.TimelinePos,
                                             string.Join(", ", charNames),
                                             charNames.Count > 0,
                                             m.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm")
                                         );
                                     }
                                 )
                                 .ToList();

            groups.Add(new SidebarQueryResult.MemoryPanelGroup(grp.Label, memoryItems));
        }

        Log.Information
        (
            "记忆面板刷新完成: 对话={SessionID}, 记忆数={Count}",
            sessionID,
            groups.Sum(g => g.Items.Count)
        );

        return new SidebarQueryResult.MemoryPanelData(groups);
    }
}
