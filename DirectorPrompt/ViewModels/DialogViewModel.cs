using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Documents;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Markdown;

namespace DirectorPrompt.ViewModels;

public sealed class DialogEntryViewModel : INotifyPropertyChanged
{
    private bool isLast;

    public long ID { get; init; }

    public long RoundID { get; init; }

    public EventType Type { get; init; }

    public string Content { get; init; } = string.Empty;

    public FlowDocument? Document { get; private set; }

    public bool IsDirector => Type == EventType.DirectorInput;

    public bool IsNarrative => Type == EventType.NarrativeOutput;

    public bool IsLast
    {
        get => isLast;
        set
        {
            if (isLast != value)
            {
                isLast = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLast)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RenderMarkdown()
    {
        if (IsNarrative)
        {
            Document = MarkdownRenderer.Render(Content);
        }
    }
}

public sealed class DialogViewModel
{
    public ObservableCollection<DialogEntryViewModel> Entries { get; } = [];

    public void Clear()
    {
        Entries.Clear();
    }

    public void AddOpeningMessage(string content)
    {
        ClearLastFlag();

        var entry = new DialogEntryViewModel
        {
            ID = Entries.Count + 1,
            RoundID = 0,
            Type = EventType.NarrativeOutput,
            Content = content,
            IsLast = true
        };

        entry.RenderMarkdown();
        Entries.Add(entry);
    }

    public void AddDirectorEntry(long roundID, string content)
    {
        ClearLastFlag();

        var entry = new DialogEntryViewModel
        {
            ID = Entries.Count + 1,
            RoundID = roundID,
            Type = EventType.DirectorInput,
            Content = content,
            IsLast = true
        };

        Entries.Add(entry);
    }

    public void AddNarrativeEntry(long roundID, string content)
    {
        ClearLastFlag();

        var entry = new DialogEntryViewModel
        {
            ID = Entries.Count + 1,
            RoundID = roundID,
            Type = EventType.NarrativeOutput,
            Content = content,
            IsLast = true
        };

        entry.RenderMarkdown();
        Entries.Add(entry);
    }

    public void RemoveEntriesByRound(long roundID)
    {
        var toRemove = Entries.Where(e => e.RoundID == roundID).ToList();

        foreach (var entry in toRemove)
        {
            Entries.Remove(entry);
        }

        var lastNarrative = Entries.LastOrDefault(e => e.IsNarrative);

        if (lastNarrative is not null)
        {
            lastNarrative.IsLast = true;
        }
        else
        {
            var lastEntry = Entries.LastOrDefault();

            if (lastEntry is not null)
            {
                lastEntry.IsLast = true;
            }
        }
    }

    private void ClearLastFlag()
    {
        foreach (var entry in Entries)
        {
            entry.IsLast = false;
        }
    }
}
