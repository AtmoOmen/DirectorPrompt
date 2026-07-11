using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Markdown;

namespace DirectorPrompt.ViewModels;

public sealed class DialogEntryViewModel : INotifyPropertyChanged
{
    private string thinking = string.Empty;
    private string errorMessage = string.Empty;

    public long ID { get; init; }

    public long RoundID { get; set; }

    public long? EventID { get; set; }

    public EventType Type { get; init; }

    public ObservableCollection<DirectorContentBlockViewModel> DirectorBlocks { get; } = [];

    public bool HasDirectorBlocks => IsDirector && DirectorBlocks.Count > 0;

    public string Content
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
            }
        }
    } = string.Empty;

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

    public string ErrorMessage
    {
        get => errorMessage;
        set
        {
            if (errorMessage != value)
            {
                errorMessage = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorMessage)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasError)));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(errorMessage);

    public bool IsStreaming
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStreaming)));
            }
        }
    }

    public bool IsEditing
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
            }
        }
    }

    public string EditingContent
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditingContent)));
            }
        }
    } = string.Empty;

    public FlowDocument? Document { get; private set; }

    public bool IsDirector => Type == EventType.DirectorInput;

    public bool IsNarrative => Type == EventType.NarrativeOutput;

    public bool IsLast
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLast)));
            }
        }
    }

    public string Role => IsDirector ?
                              "用户" :
                              "AI";

    public event PropertyChangedEventHandler? PropertyChanged;

    public DialogEntryViewModel() =>
        DirectorBlocks.CollectionChanged += OnDirectorBlocksChanged;

    private void OnDirectorBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDirectorBlocks)));

    public void RenderMarkdown()
    {
        if (HasDirectorBlocks)
        {
            foreach (var block in DirectorBlocks)
                block.RenderMarkdown();
        }
        else
        {
            Document = MarkdownRenderer.Render(Content);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Document)));
        }

        IsStreaming = false;
    }

    public void UpdateStreamingContent(string narrative, string thinking)
    {
        Content  = narrative;
        Thinking = thinking;

        Document = MarkdownRenderer.Render(narrative);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Document)));
    }

    public void SetError(string message)
    {
        IsStreaming = false;
        ErrorMessage = message;
    }

    public void StartEdit()
    {
        EditingContent = Content;
        IsEditing      = true;
    }

    public void CommitEdit()
    {
        Content   = EditingContent;
        IsEditing = false;
        DirectorBlocks.Clear();
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

    public DialogEntryViewModel AddDirectorEntry(long roundID, IReadOnlyList<(DirectiveType Type, string Content)> directives)
    {
        ClearLastFlag();

        var content = string.Join("\n", directives.Select(d => $"[{d.Type}] {d.Content}"));

        var entry = new DialogEntryViewModel
        {
            ID      = Entries.Count + 1,
            RoundID = roundID,
            Type    = EventType.DirectorInput,
            Content = content,
            IsLast  = true
        };

        foreach (var d in directives)
        {
            entry.DirectorBlocks.Add
            (
                new DirectorContentBlockViewModel
                {
                    Type    = d.Type,
                    Content = d.Content
                }
            );
        }

        Entries.Add(entry);
        DeferredRender(entry);
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
            lastEntry?.IsLast = true;
        }
    }

    private void ClearLastFlag()
    {
        foreach (var entry in Entries)
            entry.IsLast = false;
    }

    private static void DeferredRender(DialogEntryViewModel entry) =>
        Application.Current.Dispatcher.BeginInvoke
        (
            DispatcherPriority.Background,
            entry.RenderMarkdown
        );
}
