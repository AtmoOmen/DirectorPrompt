using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Localization;

namespace DirectorPrompt.ViewModels;

public sealed class DialogEntryViewModel : INotifyPropertyChanged
{
    private string thinking     = string.Empty;
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
            }
        }
    } = string.Empty;

    public bool IsMenuOpen
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMenuOpen)));
        }
    }

    public string Preview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Content))
                return string.Empty;

            var text = Content.Replace("\n", " ").Replace("\r", "").Trim();

            const int TEXT_LENGTH = 64;

            return text.Length <= TEXT_LENGTH ?
                       text :
                       string.Concat(text.AsSpan(0, TEXT_LENGTH), "…");
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDirectorContent)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowNarrativeContent)));
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDirectorContent)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowNarrativeContent)));
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

    public bool IsDirector => Type == EventType.DirectorInput;

    public bool IsNarrative => Type == EventType.NarrativeOutput;

    public bool ShowDirectorContent => HasDirectorBlocks && !IsEditing && !HasError;

    public bool ShowNarrativeContent => IsNarrative && !IsEditing && !HasError;

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
                              Loc.Get("Dialog.Role.User") :
                              Loc.Get("Dialog.Role.AI");

    public event PropertyChangedEventHandler? PropertyChanged;

    public DialogEntryViewModel() =>
        DirectorBlocks.CollectionChanged += OnDirectorBlocksChanged;

    private void OnDirectorBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDirectorBlocks)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDirectorContent)));
    }

    public void RenderMarkdown() =>
        IsStreaming = false;

    public void UpdateStreamingContent(string narrative, string thinking, bool replaceContent = false)
    {
        Content  = narrative;
        Thinking = thinking;

    }

    public void SetError(string message)
    {
        IsStreaming  = false;
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

    public void AddOpeningMessage(string content, bool renderImmediately = false)
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
    }

    public DialogEntryViewModel AddDirectorEntry(long roundID, IReadOnlyList<(DirectiveType Type, string Content)> directives, bool renderImmediately = false)
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

    public void AddNarrativeEntry(long roundID, string content, string thinking = "", bool renderImmediately = false)
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
        if (Entries.Count > 0)
            Entries[^1].IsLast = false;
    }
}
