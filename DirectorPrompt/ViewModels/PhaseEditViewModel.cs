using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Agents;

namespace DirectorPrompt.ViewModels;

public enum KnowledgeSelectionKind
{
    Group,
    Entry
}

public sealed class KnowledgeSelectionItem : ObservableObject
{
    public long ID { get; set; }

    public KnowledgeSelectionKind Kind { get; set; }

    public string Display { get; set; } = string.Empty;

    public string DisplayWithType => Kind == KnowledgeSelectionKind.Group ?
                                         $"[分组] {Display}" :
                                         Display;
}

public sealed partial class PhaseEditViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Expression { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEditing { get; set; } = true;

    [ObservableProperty]
    public partial KnowledgeSelectionItem? SelectedAvailableItem { get; set; }

    public ObservableCollection<KnowledgeSelectionItem> AvailableKnowledgeItems { get; } = [];

    public ObservableCollection<KnowledgeSelectionItem> LinkedKnowledgeItems { get; } = [];

    public DirectiveInputViewModel EnterDirectiveInput { get; } = new();

    public DirectiveInputViewModel ExitDirectiveInput { get; } = new();

    public long[] GetKnowledgeIDs() =>
        LinkedKnowledgeItems
            .Where(i => i.Kind == KnowledgeSelectionKind.Entry)
            .Select(i => i.ID)
            .ToArray();

    public long[] GetKnowledgeGroupIDs() =>
        LinkedKnowledgeItems
            .Where(i => i.Kind == KnowledgeSelectionKind.Group)
            .Select(i => i.ID)
            .ToArray();

    public void PopulateAvailableKnowledge(IEnumerable<KnowledgeGroupEditViewModel> groups)
    {
        AvailableKnowledgeItems.Clear();

        foreach (var group in groups.Where(g => !g.Active))
        {
            AvailableKnowledgeItems.Add
            (
                new KnowledgeSelectionItem
                {
                    ID      = group.ID,
                    Kind    = KnowledgeSelectionKind.Group,
                    Display = group.Name
                }
            );
        }

        foreach (var group in groups)
        {
            foreach (var entry in group.Entries.Where(e => !e.Active))
            {
                AvailableKnowledgeItems.Add
                (
                    new KnowledgeSelectionItem
                    {
                        ID      = entry.ID,
                        Kind    = KnowledgeSelectionKind.Entry,
                        Display = entry.Title
                    }
                );
            }
        }
    }

    public void AddLinkedItem(KnowledgeSelectionItem item)
    {
        if (LinkedKnowledgeItems.Any(i => i.ID == item.ID && i.Kind == item.Kind))
            return;

        LinkedKnowledgeItems.Add(item);
        AvailableKnowledgeItems.Remove(item);
        SelectedAvailableItem = null;
    }

    public void RemoveLinkedItem(KnowledgeSelectionItem item)
    {
        LinkedKnowledgeItems.Remove(item);
        AvailableKnowledgeItems.Add(item);
    }

    public void SyncFromConfig
    (
        string                       name,
        string                       expression,
        long[]                       knowledgeIds,
        long[]                       knowledgeGroupIds,
        IReadOnlyList<DirectiveItem> enterDirectives,
        IReadOnlyList<DirectiveItem> exitDirectives
    )
    {
        Name       = name;
        Expression = expression;
        IsEditing  = false;

        var kidSet = new HashSet<long>(knowledgeIds);
        var gidSet = new HashSet<long>(knowledgeGroupIds);

        var toLink = AvailableKnowledgeItems
                     .Where
                     (i => i.Kind switch
                         {
                             KnowledgeSelectionKind.Group => gidSet.Contains(i.ID),
                             KnowledgeSelectionKind.Entry => kidSet.Contains(i.ID),
                             _                            => false
                         }
                     )
                     .ToList();

        foreach (var item in toLink)
        {
            AvailableKnowledgeItems.Remove(item);
            LinkedKnowledgeItems.Add(item);
        }

        var order = 1;

        foreach (var d in enterDirectives)
        {
            EnterDirectiveInput.Directives.Add
            (
                new DirectiveItemViewModel
                {
                    Type    = d.Type,
                    Content = d.Content,
                    Order   = order++,
                    TTL     = d.TTL
                }
            );
        }

        order = 1;

        foreach (var d in exitDirectives)
        {
            ExitDirectiveInput.Directives.Add
            (
                new DirectiveItemViewModel
                {
                    Type    = d.Type,
                    Content = d.Content,
                    Order   = order++,
                    TTL     = d.TTL
                }
            );
        }
    }
}
