using System.Text.Json;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Serilog;

namespace DirectorPrompt.Agents;

public sealed class CharacterCategoryResolver
(
    ICharacterRepository characterRepository,
    IStateRepository     stateRepository
) : ICharacterCategoryResolver
{
    public async Task<CharacterCategoryResolution?> ResolveAsync
    (
        long              characterID,
        CancellationToken cancellationToken = default
    )
    {
        var character = await characterRepository.GetByIDAsync(characterID, cancellationToken);

        if (character is null)
            return null;

        var allCategories      = await characterRepository.GetCategoriesAsync(character.ProjectID, cancellationToken);
        var categoryAttributes = await stateRepository.GetAttributesAsync(character.ProjectID, StateScope.Category, cancellationToken);

        var (expandedIDs, depthMap) = ExpandCategoriesWithDepth(character.CategoryIDs, allCategories);
        var attributeIDs = ResolveAttributes(depthMap, categoryAttributes);

        return new CharacterCategoryResolution
        {
            CharacterID  = characterID,
            CategoryIDs  = expandedIDs,
            AttributeIDs = attributeIDs
        };
    }

    public async Task<CharacterCategoryResolution?> ResolveAndPersistAsync
    (
        long              characterID,
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken = default
    )
    {
        var resolution = await ResolveAsync(characterID, cancellationToken);

        if (resolution is null)
            return null;

        await characterRepository.UpdateResolvedCategoriesAsync(resolution, cancellationToken);

        var character = await characterRepository.GetByIDAsync(characterID, cancellationToken);

        if (character is not null)
            await InitializeDefaultStateValuesAsync(character.ProjectID, characterID, resolution.AttributeIDs, sessionID, roundID, cancellationToken);

        Log.Information
        (
            "分类解析完成: characterID={CharacterID}, 分类数={CategoryCount}, 属性数={AttributeCount}",
            characterID,
            resolution.CategoryIDs.Length,
            resolution.AttributeIDs.Length
        );

        return resolution;
    }

    private static (long[] CategoryIDs, Dictionary<long, int> DepthMap) ExpandCategoriesWithDepth
    (
        long[]                           directCategoryIDs,
        IReadOnlyList<CharacterCategory> allCategories
    )
    {
        var categoryMap = allCategories.ToDictionary(c => c.ID);
        var result      = new HashSet<long>();
        var depthMap    = new Dictionary<long, int>();
        var queue       = new Queue<(long ID, int Depth)>();

        foreach (var id in directCategoryIDs)
            queue.Enqueue((id, 0));

        while (queue.Count > 0)
        {
            var (id, depth) = queue.Dequeue();

            if (depthMap.TryGetValue(id, out var existingDepth) && existingDepth <= depth)
                continue;

            depthMap[id] = depth;
            result.Add(id);

            if (categoryMap.TryGetValue(id, out var category))
            {
                foreach (var parentId in category.ParentCategoryIDs)
                    queue.Enqueue((parentId, depth + 1));
            }
        }

        return ([.. result], depthMap);
    }

    private static long[] ResolveAttributes
    (
        Dictionary<long, int>         depthMap,
        IReadOnlyList<StateAttribute> categoryAttributes
    )
    {
        var byName = new Dictionary<string, (long AttributeID, int Depth)>();

        foreach (var attr in categoryAttributes)
        {
            if (attr.CategoryID is null)
                continue;

            if (!depthMap.TryGetValue(attr.CategoryID.Value, out var depth))
                continue;

            if (byName.TryGetValue(attr.Name, out var existing))
            {
                if (depth < existing.Depth)
                    byName[attr.Name] = (attr.ID, depth);
            }
            else
                byName[attr.Name] = (attr.ID, depth);
        }

        return byName.Values.Select(v => v.AttributeID).ToArray();
    }

    private async Task InitializeDefaultStateValuesAsync
    (
        long              projectID,
        long              characterID,
        long[]            attributeIDs,
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken
    )
    {
        var existingValues = await characterRepository.GetCharacterStateValuesAsync(characterID, cancellationToken);
        var existingSet    = existingValues.Select(v => v.AttributeID).ToHashSet();

        var allAttributes = await stateRepository.GetAttributesAsync(projectID, StateScope.Category, cancellationToken);
        var attrLookup    = allAttributes.ToDictionary(a => a.ID);

        foreach (var attrID in attributeIDs)
        {
            if (existingSet.Contains(attrID))
                continue;

            if (!attrLookup.TryGetValue(attrID, out var attr))
                continue;

            var defaultValue = GetDefaultValue(attr);

            await characterRepository.SetCharacterStateValueAsync(characterID, attrID, defaultValue, sessionID, roundID, cancellationToken);
        }
    }

    private static string GetDefaultValue(StateAttribute attr)
    {
        if (attr.ValueType == StateValueType.Numeric)
            return "0";

        if (attr.ValueType == StateValueType.Enum)
        {
            var config = string.IsNullOrWhiteSpace(attr.Config) ?
                             null :
                             JsonSerializer.Deserialize<StateAttributeConfig>(attr.Config, JsonOptions.Default);

            if (config is not null && config.Options.Count > 0)
                return config.Options[0];
        }

        return string.Empty;
    }
}
