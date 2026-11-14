using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using DirectorPrompt.Agents;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Localization;
using Markdig.Syntax;

namespace DirectorPrompt.ViewModels;

public sealed class DialogEntryViewModel : INotifyPropertyChanged
{
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
                field            = value;
                MarkdownDocument = null;
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

    public ObservableCollection<NarrationActivityViewModel> Activities { get; } = [];

    public bool HasActivities => Activities.Count > 0;

    public void UpdateActivity
    (
        NarrationActivity activity
    )
    {
        var existing = Activities.FirstOrDefault(item => item.Activity.ID == activity.ID);

        if (existing is null)
        {
            Activities.Add(new NarrationActivityViewModel(activity));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActivities)));
        }
        else
            existing.Update(activity);
    }

    public string ErrorMessage
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorMessage)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasError)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDirectorContent)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowNarrativeContent)));
            }
        }
    } = string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public MarkdownDocument? MarkdownDocument
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MarkdownDocument)));
        }
    }

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

    private void OnDirectorBlocksChanged
    (
        object?                          sender,
        NotifyCollectionChangedEventArgs e
    )
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDirectorBlocks)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDirectorContent)));
    }

    public void RenderMarkdown() =>
        IsStreaming = false;

    public void SetError
    (
        string message
    )
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
