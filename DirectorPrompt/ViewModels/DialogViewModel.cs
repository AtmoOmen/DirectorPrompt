using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Markdown;

namespace DirectorPrompt.ViewModels;

public sealed class DialogEntryViewModel : INotifyPropertyChanged
{
    private bool   isLast;
    private string content  = string.Empty;
    private string thinking = string.Empty;
    private bool   isStreaming;
    private bool   isEditing;
    private string editingContent = string.Empty;

    public long ID { get; init; }

    public long RoundID { get; set; }

    public long? EventID { get; set; }

    public EventType Type { get; init; }

    public string Content
    {
        get => content;
        set
        {
            if (content != value)
            {
                content = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
            }
        }
    }

    public string Thinking
    {
        get => thinking;
        set
        {
            if (thinking != value)
            {
                thinking = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thinking)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasThinking)));
            }
        }
    }

    public bool HasThinking => !string.IsNullOrWhiteSpace(thinking);

    public bool IsStreaming
    {
        get => isStreaming;
        set
        {
            if (isStreaming != value)
            {
                isStreaming = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStreaming)));
            }
        }
    }

    public bool IsEditing
    {
        get => isEditing;
        set
        {
            if (isEditing != value)
            {
                isEditing = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
            }
        }
    }

    public string EditingContent
    {
        get => editingContent;
        set
        {
            if (editingContent != value)
            {
                editingContent = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditingContent)));
            }
        }
    }

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

    public string Role => IsDirector ? "导演" : "AI";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RenderMarkdown()
    {
        Document    = MarkdownRenderer.Render(Content);
        IsStreaming = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Document)));
    }

    public void UpdateStreamingContent(string narrative, string thinking)
    {
        Content  = narrative;
        Thinking = thinking;
    }

    public void StartEdit()
    {
        EditingContent = Content;
        IsEditing      = true;
    }

    public void CommitEdit()
    {
        Content    = EditingContent;
        IsEditing  = false;
        RenderMarkdown();
    }

    public void CancelEdit()
    {
        IsEditing      = false;
        EditingContent = string.Empty;
    }
}

public sealed class DialogViewModel
{
    public ObservableCollection<DialogEntryViewModel> Entries { get; } = [];

    public void Clear() =>
        Entries.Clear();

    public void AddOpeningMessage(string content)
    {
        ClearLastFlag();

        var entry = new DialogEntryViewModel
        {
            ID      = Entries.Count + 1,
            RoundID = 0,
            Type    = EventType.NarrativeOutput,
            Content = content,
            IsLast  = true
        };

        Entries.Add(entry);
        DeferredRender(entry);
    }

    public void AddDirectorEntry(long roundID, string content)
    {
        ClearLastFlag();

        var entry = new DialogEntryViewModel
        {
            ID      = Entries.Count + 1,
            RoundID = roundID,
            Type    = EventType.DirectorInput,
            Content = content,
            IsLast  = true
        };

        Entries.Add(entry);
        DeferredRender(entry);
    }

    public DialogEntryViewModel BeginStreamingNarrative(long roundID)
    {
        ClearLastFlag();

        var entry = new DialogEntryViewModel
        {
            ID          = Entries.Count + 1,
            RoundID     = roundID,
            Type        = EventType.NarrativeOutput,
            Content     = string.Empty,
            Thinking    = string.Empty,
            IsStreaming = true,
            IsLast      = true
        };

        Entries.Add(entry);
        return entry;
    }

    public void AddNarrativeEntry(long roundID, string content, string thinking = "")
    {
        ClearLastFlag();

        var entry = new DialogEntryViewModel
        {
            ID       = Entries.Count + 1,
            RoundID  = roundID,
            Type     = EventType.NarrativeOutput,
            Content  = content,
            Thinking = thinking,
            IsLast   = true
        };

        Entries.Add(entry);
        DeferredRender(entry);
    }

    public void RemoveEntriesByRound(long roundID)
    {
        var toRemove = Entries.Where(e => e.RoundID == roundID).ToList();

        foreach (var entry in toRemove)
            Entries.Remove(entry);

        var lastNarrative = Entries.LastOrDefault(e => e.IsNarrative);

        if (lastNarrative is not null)
            lastNarrative.IsLast = true;
        else
        {
            var lastEntry = Entries.LastOrDefault();

            if (lastEntry is not null)
                lastEntry.IsLast = true;
        }
    }

    private void ClearLastFlag()
    {
        foreach (var entry in Entries)
            entry.IsLast = false;
    }

    private static void DeferredRender(DialogEntryViewModel entry)
    {
        Application.Current.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            entry.RenderMarkdown);
    }
}
