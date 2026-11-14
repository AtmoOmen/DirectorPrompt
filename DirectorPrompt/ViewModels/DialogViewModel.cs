using System.Collections.ObjectModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.ViewModels;

public sealed class DialogViewModel
{
    public ObservableCollection<DialogEntryViewModel> Entries { get; } = [];

    public void Clear() =>
        Entries.Clear();

    public void AddOpeningMessage(string content, bool renderImmediately = false)
    {
        ClearLastFlag();

        Entries.Add
        (
            new DialogEntryViewModel
            {
                ID      = Entries.Count + 1,
                RoundID = 0,
                Type    = EventType.NarrativeOutput,
                Content = content,
                IsLast  = true
            }
        );
    }

    public DialogEntryViewModel AddDirectorEntry
    (
        long                                                roundID,
        IReadOnlyList<(DirectiveType Type, string Content)> directives,
        bool                                                renderImmediately = false
    )
    {
        ClearLastFlag();

        var entry = new DialogEntryViewModel
        {
            ID      = Entries.Count + 1,
            RoundID = roundID,
            Type    = EventType.DirectorInput,
            Content = string.Join("\n", directives.Select(directive => $"[{directive.Type}] {directive.Content}")),
            IsLast  = true
        };

        foreach (var directive in directives)
            entry.DirectorBlocks.Add
            (
                new DirectorContentBlockViewModel
                {
                    Type    = directive.Type,
                    Content = directive.Content
                }
            );

        Entries.Add(entry);
        return entry;
    }

    public DialogEntryViewModel BeginStreamingNarrative(long roundID)
    {
        ClearLastFlag();

        var entry = new DialogEntryViewModel
        {
            ID          = Entries.Count + 1,
            RoundID     = roundID,
            Type        = EventType.NarrativeOutput,
            IsStreaming = true,
            IsLast      = true
        };

        Entries.Add(entry);
        return entry;
    }

    public void AddNarrativeEntry(long roundID, string content, bool renderImmediately = false)
    {
        ClearLastFlag();

        Entries.Add
        (
            new DialogEntryViewModel
            {
                ID      = Entries.Count + 1,
                RoundID = roundID,
                Type    = EventType.NarrativeOutput,
                Content = content,
                IsLast  = true
            }
        );
    }

    public void RemoveEntriesByRound(long roundID)
    {
        var toRemove = Entries.Where(entry => entry.RoundID == roundID).ToList();

        foreach (var entry in toRemove)
            Entries.Remove(entry);

        var lastNarrative = Entries.LastOrDefault(entry => entry.IsNarrative);

        if (lastNarrative is not null)
            lastNarrative.IsLast = true;
        else
        {
            var lastEntry = Entries.LastOrDefault();
            lastEntry?.IsLast = true;
        }
    }

    private void ClearLastFlag()
    {
        if (Entries.Count > 0)
            Entries[^1].IsLast = false;
    }
}
