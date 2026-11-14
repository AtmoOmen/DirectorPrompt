using System.Text.Json;
using DirectorPrompt.Agents.Config;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Serilog;

namespace DirectorPrompt.Agents;

public sealed class DialogHistoryService
(
    IEventRepository eventRepository
)
{
    public async Task<DialogHistoryResult> LoadAsync
    (
        long              sessionID,
        long?             beforeRoundID = null,
        CancellationToken token         = default
    )
    {
        var events = new List<PlaythroughEvent>();

        if (beforeRoundID is null)
            events.AddRange(await eventRepository.GetByRoundAsync(sessionID, 0, token));

        var page = await eventRepository.GetDialogPageAsync
                   (
                       new DialogPageQuery(sessionID, beforeRoundID, 20),
                       token
                   );
        events.AddRange(page.Events);

        if (token.IsCancellationRequested)
            return new DialogHistoryResult([], null);

        var directorEvents = events
                             .Where(e => e.Type == EventType.DirectorInput)
                             .GroupBy(e => e.RoundID)
                             .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.ID).First());

        var narrativeEvents = events
                              .Where(e => e.Type == EventType.NarrativeOutput)
                              .GroupBy(e => e.RoundID)
                              .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.ID).First());

        var traces = events.Where(item => item.Type == EventType.NarrationTrace)
                           .GroupBy(item => item.RoundID)
                           .ToDictionary(group => group.Key, group => group.MaxBy(item => item.ID)!);

        var roundIDs = directorEvents.Keys
                                     .Concat(narrativeEvents.Keys)
                                     .Distinct()
                                     .OrderBy(r => r)
                                     .ToList();

        var rounds = new List<DialogHistoryResult.RoundEntry>();

        foreach (var roundID in roundIDs)
        {
            long?                                               directorEventID = null;
            IReadOnlyList<(DirectiveType Type, string Content)> directorBlocks  = [];

            if (directorEvents.TryGetValue(roundID, out var directorEvent))
            {
                directorEventID = directorEvent.ID;
                directorBlocks  = EventDataSerializer.ParseDirectiveBlocks(directorEvent.Data);
            }

            long? narrativeEventID = null;
            var   narrativeText    = string.Empty;

            if (narrativeEvents.TryGetValue(roundID, out var narrativeEvent))
            {
                narrativeEventID = narrativeEvent.ID;
                narrativeText    = narrativeEvent.Data;
            }

            IReadOnlyList<NarrationActivity> activities = [];

            if (traces.TryGetValue(roundID, out var trace))
                activities = JsonSerializer.Deserialize<NarrationTrace>(trace.Data, JsonOptions.Compact)?.Activities ?? [];

            rounds.Add(new DialogHistoryResult.RoundEntry(roundID, directorEventID, directorBlocks, narrativeEventID, narrativeText, activities));
        }

        Log.Information
        (
            "对话历史加载完成: 对话={SessionID}, 轮次数={RoundCount}",
            sessionID,
            rounds.Count
        );

        return new DialogHistoryResult(rounds, page.PreviousRoundID);
    }
}
