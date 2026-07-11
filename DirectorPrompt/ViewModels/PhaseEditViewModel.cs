using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DirectorPrompt.Agents;

namespace DirectorPrompt.ViewModels;

public enum KnowledgeSelectionKind
{
    Group,
    Entry
}

public sealed partial class KnowledgeSelectionItem : ObservableObject
{
    public long ID { get; set; }

    public KnowledgeSelectionKind Kind { get; set; }

    public string Display { get; set; } = string.Empty;

    public string DisplayWithType => Kind == KnowledgeSelectionKind.Group ?
                                         $"[分组] {Display}" :
                                         Display;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public sealed partial class PhaseEditViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Expression { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEditing { get; set; } = true;

    public ObservableCollection<KnowledgeSelectionItem> KnowledgeItems { get; } = [];

    public DirectiveInputViewModel EnterDirectiveInput { get; } = new();

    public DirectiveInputViewModel ExitDirectiveInput { get; } = new();

    public long[] GetKnowledgeIDs() =>
        KnowledgeItems
            .Where(i => i is { Kind: KnowledgeSelectionKind.Entry, IsSelected: true })
            .Select(i => i.ID)
            .ToArray();

    public long[] GetKnowledgeGroupIDs() =>
        KnowledgeItems
            .Where(i => i is { Kind: KnowledgeSelectionKind.Group, IsSelected: true })
            .Select(i => i.ID)
            .ToArray();

    public void PopulateAvailableKnowledge(IEnumerable<KnowledgeGroupEditViewModel> groups)
    {
        KnowledgeItems.Clear();

        foreach (var group in groups.Where(g => !g.Active))
        {
            KnowledgeItems.Add
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
                KnowledgeItems.Add
                (
                    new KnowledgeSelectionItem
                    {
                        ID      = entry.ID,
                        Kind    = KnowledgeSelectionKind.Entry,
                        Display = entry.Remarks
                    }
                );
            }
        }
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

        foreach (var item in KnowledgeItems)
        {
            item.IsSelected = item.Kind switch
            {
                KnowledgeSelectionKind.Group => gidSet.Contains(item.ID),
                KnowledgeSelectionKind.Entry => kidSet.Contains(item.ID),
                _                            => false
            };
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
