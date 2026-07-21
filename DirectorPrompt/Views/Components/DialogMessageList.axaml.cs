using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml;
using DirectorPrompt.ViewModels;

namespace DirectorPrompt.Views.Components;

public partial class DialogMessageList : UserControl
{
    public static readonly StyledProperty<IEnumerable?> StableEntriesProperty =
        AvaloniaProperty.Register<DialogMessageList, IEnumerable?>(nameof(StableEntries));

    public static readonly StyledProperty<DialogEntryViewModel?> StreamingEntryProperty =
        AvaloniaProperty.Register<DialogMessageList, DialogEntryViewModel?>(nameof(StreamingEntry));

    public static readonly StyledProperty<bool> HasStreamingEntryProperty =
        AvaloniaProperty.Register<DialogMessageList, bool>(nameof(HasStreamingEntry));

    private readonly ScrollViewer     dialogScrollViewer;
    private readonly ListBox          stableEntriesListBox;
    private readonly ContentPresenter streamingEntryPresenter;

    public IEnumerable? StableEntries
    {
        get => GetValue(StableEntriesProperty);
        set => SetValue(StableEntriesProperty, value);
    }

    public DialogEntryViewModel? StreamingEntry
    {
        get => GetValue(StreamingEntryProperty);
        set => SetValue(StreamingEntryProperty, value);
    }

    public bool HasStreamingEntry
    {
        get => GetValue(HasStreamingEntryProperty);
        private set => SetValue(HasStreamingEntryProperty, value);
    }

    public ScrollViewer ScrollViewer => dialogScrollViewer;

    static DialogMessageList() =>
        StreamingEntryProperty.Changed.AddClassHandler<DialogMessageList>
        (static (list, _) => list.HasStreamingEntry = list.StreamingEntry is not null
        );

    public DialogMessageList()
    {
        AvaloniaXamlLoader.Load(this);
        dialogScrollViewer     = this.FindControl<ScrollViewer>(nameof(DialogScrollViewer))!;
        stableEntriesListBox   = this.FindControl<ListBox>(nameof(StableEntriesListBox))!;
        streamingEntryPresenter = this.FindControl<ContentPresenter>(nameof(StreamingEntryPresenter))!;
    }

    public void ScrollIntoView(DialogEntryViewModel entry)
    {
        if (ReferenceEquals(entry, StreamingEntry))
        {
            streamingEntryPresenter.BringIntoView();
            return;
        }

        stableEntriesListBox.ScrollIntoView(entry);
    }

    public Control? ContainerFromItem(DialogEntryViewModel entry) =>
        ReferenceEquals(entry, StreamingEntry) ?
            streamingEntryPresenter :
            stableEntriesListBox.ContainerFromItem(entry);

    public IEnumerable<(DialogEntryViewModel Entry, Control Container)> GetRealizedEntries()
    {
        foreach (var container in stableEntriesListBox.GetRealizedContainers())
        {
            if (stableEntriesListBox.ItemFromContainer(container) is DialogEntryViewModel entry)
                yield return (entry, container);
        }

        if (StreamingEntry is not null && streamingEntryPresenter.IsVisible)
            yield return (StreamingEntry, streamingEntryPresenter);
    }
}
