using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using DirectorPrompt.Domain.Enums;

namespace DirectorPrompt.ViewModels;

public sealed class DialogViewModel : INotifyPropertyChanged
{
    private readonly HashSet<DialogEntryViewModel>              trackedEntries = [];
    private readonly ObservableCollection<DialogEntryViewModel> stableEntries  = [];

    public ObservableCollection<DialogEntryViewModel> Entries { get; } = [];

    public ReadOnlyObservableCollection<DialogEntryViewModel> StableEntries { get; }

    public DialogEntryViewModel? StreamingEntry
    {
        get;
        private set
        {
            if (ReferenceEquals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StreamingEntry)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DialogViewModel()
    {
        StableEntries             =  new ReadOnlyObservableCollection<DialogEntryViewModel>(stableEntries);
        Entries.CollectionChanged += OnEntriesChanged;
    }

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

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        SynchronizeTrackedEntries();
        SynchronizePresentationEntries();
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (!string.IsNullOrEmpty(eventArgs.PropertyName) && eventArgs.PropertyName != nameof(DialogEntryViewModel.IsStreaming))
            return;

        SynchronizePresentationEntries();
    }

    private void SynchronizeTrackedEntries()
    {
        var removedEntries = trackedEntries.Where(entry => !Entries.Contains(entry)).ToArray();

        foreach (var entry in removedEntries)
        {
            entry.PropertyChanged -= OnEntryPropertyChanged;
            trackedEntries.Remove(entry);
        }

        foreach (var entry in Entries)
        {
            if (trackedEntries.Add(entry))
                entry.PropertyChanged += OnEntryPropertyChanged;
        }
    }

    private void SynchronizePresentationEntries()
    {
        var currentStreamingEntry = Entries.LastOrDefault(entry => entry.IsStreaming);
        StreamingEntry = currentStreamingEntry;

        var stableIndex = 0;

        foreach (var entry in Entries)
        {
            if (ReferenceEquals(entry, currentStreamingEntry))
                continue;

            if (stableIndex < stableEntries.Count && ReferenceEquals(stableEntries[stableIndex], entry))
            {
                stableIndex++;
                continue;
            }

            var existingIndex = stableEntries.IndexOf(entry);

            if (existingIndex >= 0)
                stableEntries.Move(existingIndex, stableIndex);
            else
                stableEntries.Insert(stableIndex, entry);

            stableIndex++;
        }

        while (stableEntries.Count > stableIndex)
            stableEntries.RemoveAt(stableEntries.Count - 1);
    }
}
