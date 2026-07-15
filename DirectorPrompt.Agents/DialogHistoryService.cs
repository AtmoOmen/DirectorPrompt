using DirectorPrompt.Agents.Config;
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
    public async Task<DialogHistoryResult> LoadAsync(long sessionID, CancellationToken token = default)
    {
        var events = new List<PlaythroughEvent>();
        events.AddRange(await eventRepository.GetByRoundAsync(sessionID, 0, token));

        long? beforeRoundID = null;

        do
        {
            var page = await eventRepository.GetDialogPageAsync
                       (
                           new DialogPageQuery(sessionID, beforeRoundID, 100),
                           token
                       );

            events.AddRange(page.Events);
            beforeRoundID = page.PreviousRoundID;
        }
        while (beforeRoundID is not null && !token.IsCancellationRequested);

        if (token.IsCancellationRequested)
            return new DialogHistoryResult([]);

        var directorEvents = events
                             .Where(e => e.Type == EventType.DirectorInput)
                             .GroupBy(e => e.RoundID)
                             .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.ID).First());

        var narrativeEvents = events
                              .Where(e => e.Type == EventType.NarrativeOutput)
                              .GroupBy(e => e.RoundID)
                              .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.ID).First());

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

            rounds.Add(new DialogHistoryResult.RoundEntry(roundID, directorEventID, directorBlocks, narrativeEventID, narrativeText));
        }

        Log.Information
        (
            "对话历史加载完成: 对话={SessionID}, 轮次数={RoundCount}",
            sessionID,
            rounds.Count
        );

        return new DialogHistoryResult(rounds);
    }
}
