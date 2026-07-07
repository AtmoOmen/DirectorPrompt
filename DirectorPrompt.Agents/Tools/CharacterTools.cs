using System.Globalization;
using System.Text.Json;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Tools;

public sealed class CharacterTools
(
    ICharacterRepository         characterRepository,
    IStateRepository             stateRepository,
    ICharacterCategoryResolver   categoryResolver
)
{
    public IList<AIFunction> Create(ToolExecutionContext context) =>
    [
        AIFunctionFactory.Create
        (
            (string name) => GetCharacterAsync(context, name),
            "get_character",
            """
            查询特定人物
            name: 人物名
            """
        ),
        AIFunctionFactory.Create
        (
            () => GetSceneCharactersAsync(context),
            "get_scene_characters",
            "查询当前场景的在场人物列表"
        ),
        AIFunctionFactory.Create
        (
            (string name) => GetRelationsAsync(context, name),
            "get_relations",
            """
            查询特定人物的关系网络
            name: 人物名
            """
        ),
        AIFunctionFactory.Create
        (
            (string name, string attribute) => GetCharacterStateAsync(context, name, attribute),
            "get_character_state",
            """
            查询人物的状态属性值
            name: 人物名
            attribute: 属性名
            """
        ),
        AIFunctionFactory.Create
        (
            (string name, string description, string categoryIDs, string reason) =>
                AddCharacterAsync(context, name, description, categoryIDs, reason),
            "add_character",
            """
            新增人物
            name: 人物名
            description: 描述
            categoryIDs: 分类 ID 列表 (逗号分隔)
            reason: 新增原因
            """
        ),
        AIFunctionFactory.Create
        (
            (string name, string reason, string? status) => RemoveCharacterAsync(context, name, reason, status),
            "remove_character",
            """
            标记人物离场或死亡
            name: 人物名
            reason: 原因
            status: left 或 dead (可选, 默认 left)
            """
        ),
        AIFunctionFactory.Create
        (
            (string name, string description, string? categoryIDs, string reason) =>
                UpdateCharacterAsync(context, name, description, categoryIDs, reason),
            "update_character",
            """
            更新人物描述和分类
            name: 人物名
            description: 新描述
            categoryIDs: 新分类 ID 列表 (逗号分隔, 可选)
            reason: 原因
            """
        ),
        AIFunctionFactory.Create
        (
            (string sourceName, string targetName, string relationType, string? description, double? intensity, string reason) =>
                SetRelationAsync(context, sourceName, targetName, relationType, description, intensity, reason),
            "set_relation",
            """
            设置或更新人物关系
            sourceName: 主体人物
            targetName: 客体人物
            relationType: 关系类型
            description: 关系描述 (可选)
            intensity: 关系强度 0-1 (可选)
            reason: 原因
            """
        ),
        AIFunctionFactory.Create
        (
            (string name) => EnterSceneAsync(context, name),
            "enter_scene",
            """
            标记人物进入当前场景
            name: 人物名
            """
        ),
        AIFunctionFactory.Create
        (
            (string name) => LeaveSceneAsync(context, name),
            "leave_scene",
            """
            标记人物离开当前场景
            name: 人物名
            """
        ),
        AIFunctionFactory.Create
        (
            (string characterName, string attribute, double delta, string reason) =>
                UpdateCharacterStateAsync(context, characterName, attribute, delta, reason),
            "update_character_state",
            """
            数值增减人物状态属性
            characterName: 人物名
            attribute: 属性名
            delta: 变化量
            reason: 原因
            """
        ),
        AIFunctionFactory.Create
        (
            (string characterName, string attribute, string value, string reason) =>
                SetCharacterStateAsync(context, characterName, attribute, value, reason),
            "set_character_state",
            """
            设置人物状态属性为指定值
            characterName: 人物名
            attribute: 属性名
            value: 新值
            reason: 原因
            """
        )
    ];

    private async Task<string> GetCharacterAsync(ToolExecutionContext context, string name)
    {
        var character = await characterRepository.GetByNameAsync(context.SessionID, name);

        if (character is null)
            return JsonSerializer.Serialize(new { error = $"人物 {name} 不存在" });

        var categories = await characterRepository.GetCategoriesAsync(context.ProjectID);
        var categoryNames = categories
                            .Where(c => character.CategoryIDs.Contains(c.ID))
                            .Select(c => c.Name)
                            .ToArray();

        var stateValues = await GetCharacterStateValuesAsync(context, character.ID);
        var relations   = await GetCharacterRelationsAsync(context, character.ID);

        return JsonSerializer.Serialize
        (
            new
            {
                name        = character.Name,
                description = character.Description,
                categories  = categoryNames,
                status      = character.Status.ToString(),
                stateValues,
                relations
            }
        );
    }

    private async Task<string> GetSceneCharactersAsync(ToolExecutionContext context)
    {
        if (context.SceneID is null)
            return JsonSerializer.Serialize(Array.Empty<object>());

        var characters = await characterRepository.GetBySceneAsync(context.SceneID.Value);
        var categories = await characterRepository.GetCategoriesAsync(context.ProjectID);

        var result = new List<object>();

        foreach (var character in characters)
        {
            var categoryNames = categories
                                .Where(c => character.CategoryIDs.Contains(c.ID))
                                .Select(c => c.Name)
                                .ToArray();

            result.Add
            (
                new
                {
                    name        = character.Name,
                    description = character.Description,
                    categories  = categoryNames,
                    status      = character.Status.ToString()
                }
            );
        }

        return JsonSerializer.Serialize(result);
    }

    private async Task<string> GetRelationsAsync(ToolExecutionContext context, string characterName)
    {
        var character = await characterRepository.GetByNameAsync(context.SessionID, characterName);

        if (character is null)
            return JsonSerializer.Serialize(new { error = $"人物 {characterName} 不存在" });

        var relations     = await characterRepository.GetRelationsByCharacterAsync(character.ID);
        var allCharacters = await characterRepository.GetBySessionAsync(context.SessionID);
        var charLookup    = allCharacters.ToDictionary(c => c.ID);

        var result = new List<object>();

        foreach (var r in relations)
        {
            var otherID    = r.SourceCharacterID == character.ID ? r.TargetCharacterID : r.SourceCharacterID;
            var otherName  = charLookup.TryGetValue(otherID, out var other) ? other.Name : $"ID:{otherID}";
            var direction  = r.SourceCharacterID == character.ID ? "outgoing" : "incoming";

            result.Add
            (
                new
                {
                    target      = otherName,
                    type        = r.RelationType,
                    description = r.Description,
                    intensity   = r.Intensity,
                    direction
                }
            );
        }

        return JsonSerializer.Serialize(result);
    }

    private async Task<string> GetCharacterStateAsync
    (
        ToolExecutionContext context,
        string               characterName,
        string               attribute
    )
    {
        var character = await characterRepository.GetByNameAsync(context.SessionID, characterName);

        if (character is null)
            return JsonSerializer.Serialize(new { error = $"人物 {characterName} 不存在" });

        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID, StateScope.Category);
        var attr       = attributes.FirstOrDefault(a => a.Name == attribute);

        if (attr is null)
            return JsonSerializer.Serialize(new { error = $"状态属性 {attribute} 不存在" });

        var values = await characterRepository.GetCharacterStateValuesAsync(character.ID);
        var value  = values.FirstOrDefault(v => v.AttributeID == attr.ID);

        return JsonSerializer.Serialize
        (
            new
            {
                character = characterName,
                attribute,
                value = value?.Value
            }
        );
    }

    private async Task<string> AddCharacterAsync
    (
        ToolExecutionContext context,
        string               name,
        string               description,
        string               categoryIDs,
        string               reason
    )
    {
        Log.Information("工具调用: add_character(name={Name}, reason={Reason})", name, reason);

        var existing = await characterRepository.GetByNameAsync(context.SessionID, name);

        if (existing is not null)
            return JsonSerializer.Serialize(new { error = $"人物 {name} 已存在" });

        var categoryIDList = string.IsNullOrWhiteSpace(categoryIDs) ?
                                 [] :
                                 categoryIDs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                            .Select(long.Parse)
                                            .ToArray();

        var character = new Character
        {
            ProjectID   = context.ProjectID,
            SessionID   = context.SessionID,
            Name        = name,
            Description = description,
            CategoryIDs = categoryIDList,
            Status      = CharacterStatus.Active
        };

        var created = await characterRepository.CreateAsync(character);

        await categoryResolver.ResolveAndPersistAsync(created.ID);

        Log.Information("工具调用完成: add_character, characterID={ID}, name={Name}", created.ID, name);

        return JsonSerializer.Serialize(new { characterID = created.ID });
    }

    private async Task<string> RemoveCharacterAsync
    (
        ToolExecutionContext context,
        string               name,
        string               reason,
        string?              status
    )
    {
        Log.Information("工具调用: remove_character(name={Name}, reason={Reason}, status={Status})", name, reason, status);

        var character = await characterRepository.GetByNameAsync(context.SessionID, name);

        if (character is null)
            return JsonSerializer.Serialize(new { error = $"人物 {name} 不存在" });

        var targetStatus = status?.ToLowerInvariant() switch
        {
            "dead"     => CharacterStatus.Dead,
            "left"     => CharacterStatus.Left,
            _          => CharacterStatus.Left
        };

        await characterRepository.SetStatusAsync(character.ID, targetStatus);

        return JsonSerializer.Serialize(new { name, status = targetStatus.ToString().ToLowerInvariant() });
    }

    private async Task<string> UpdateCharacterAsync
    (
        ToolExecutionContext context,
        string               name,
        string               description,
        string?              categoryIDs,
        string               reason
    )
    {
        var character = await characterRepository.GetByNameAsync(context.SessionID, name);

        if (character is null)
            return JsonSerializer.Serialize(new { error = $"人物 {name} 不存在" });

        var newCategoryIDs = character.CategoryIDs;

        if (!string.IsNullOrWhiteSpace(categoryIDs))
        {
            newCategoryIDs = categoryIDs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                        .Select(long.Parse)
                                        .ToArray();
        }

        var updated = character with { Description = description, CategoryIDs = newCategoryIDs };

        await characterRepository.UpdateAsync(updated);

        if (!string.IsNullOrWhiteSpace(categoryIDs))
            await categoryResolver.ResolveAndPersistAsync(character.ID);

        return JsonSerializer.Serialize(new { name, success = true });
    }

    private async Task<string> SetRelationAsync
    (
        ToolExecutionContext context,
        string               sourceName,
        string               targetName,
        string               relationType,
        string?              description,
        double?              intensity,
        string               reason
    )
    {
        var source = await characterRepository.GetByNameAsync(context.SessionID, sourceName);
        var target = await characterRepository.GetByNameAsync(context.SessionID, targetName);

        if (source is null)
            return JsonSerializer.Serialize(new { error = $"人物 {sourceName} 不存在" });

        if (target is null)
            return JsonSerializer.Serialize(new { error = $"人物 {targetName} 不存在" });

        float? intensityFloat = intensity is null ? null : (float)intensity.Value;

        await characterRepository.SetRelationAsync
        (
            context.SessionID,
            source.ID,
            target.ID,
            relationType,
            description,
            intensityFloat,
            RelationChangeSource.MemorySubAgent,
            reason,
            context.SceneID ?? 0
        );

        return JsonSerializer.Serialize
        (
            new
            {
                source = sourceName,
                target = targetName,
                relationType,
                success = true
            }
        );
    }

    private async Task<string> EnterSceneAsync(ToolExecutionContext context, string name)
    {
        Log.Information("工具调用: enter_scene(name={Name})", name);

        if (context.SceneID is null)
            return JsonSerializer.Serialize(new { error = "当前没有活跃场景" });

        var character = await characterRepository.GetByNameAsync(context.SessionID, name);

        if (character is null)
            return JsonSerializer.Serialize(new { error = $"人物 {name} 不存在" });

        await characterRepository.EnterSceneAsync(character.ID, context.SceneID.Value);

        Log.Information("工具调用完成: enter_scene, name={Name}, sceneID={SceneID}", name, context.SceneID.Value);

        return JsonSerializer.Serialize(new { name, sceneID = context.SceneID.Value });
    }

    private async Task<string> LeaveSceneAsync(ToolExecutionContext context, string name)
    {
        Log.Information("工具调用: leave_scene(name={Name})", name);

        if (context.SceneID is null)
            return JsonSerializer.Serialize(new { error = "当前没有活跃场景" });

        var character = await characterRepository.GetByNameAsync(context.SessionID, name);

        if (character is null)
            return JsonSerializer.Serialize(new { error = $"人物 {name} 不存在" });

        await characterRepository.LeaveSceneAsync(character.ID, context.SceneID.Value);

        return JsonSerializer.Serialize(new { name, leftScene = true });
    }

    private async Task<string> UpdateCharacterStateAsync
    (
        ToolExecutionContext context,
        string               characterName,
        string               attribute,
        double               delta,
        string               reason
    )
    {
        var character = await characterRepository.GetByNameAsync(context.SessionID, characterName);

        if (character is null)
            return JsonSerializer.Serialize(new { error = $"人物 {characterName} 不存在" });

        var (attr, error) = await ResolveCategoryAttributeAsync(context, attribute);

        if (attr is null)
            return error;

        if (attr.Driver == Driver.System)
            return JsonSerializer.Serialize(new { error = $"状态属性 {attribute} 为 system 驱动, AI 不可直接修改" });

        var values       = await characterRepository.GetCharacterStateValuesAsync(character.ID);
        var currentValue = values.FirstOrDefault(v => v.AttributeID == attr.ID);
        var currentNum   = double.Parse(currentValue?.Value ?? "0", CultureInfo.InvariantCulture);
        var newValue     = currentNum + delta;

        await characterRepository.SetCharacterStateValueAsync(character.ID, attr.ID, newValue.ToString(CultureInfo.InvariantCulture));

        return JsonSerializer.Serialize
        (
            new
            {
                oldValue = currentValue?.Value,
                newValue = newValue.ToString(CultureInfo.InvariantCulture)
            }
        );
    }

    private async Task<string> SetCharacterStateAsync
    (
        ToolExecutionContext context,
        string               characterName,
        string               attribute,
        string               value,
        string               reason
    )
    {
        var character = await characterRepository.GetByNameAsync(context.SessionID, characterName);

        if (character is null)
            return JsonSerializer.Serialize(new { error = $"人物 {characterName} 不存在" });

        var (attr, error) = await ResolveCategoryAttributeAsync(context, attribute);

        if (attr is null)
            return error;

        if (attr.Driver == Driver.System)
            return JsonSerializer.Serialize(new { error = $"状态属性 {attribute} 为 system 驱动, AI 不可直接修改" });

        await characterRepository.SetCharacterStateValueAsync(character.ID, attr.ID, value);

        return JsonSerializer.Serialize
        (
            new
            {
                character = characterName,
                attribute,
                value
            }
        );
    }

    private async Task<List<object>> GetCharacterStateValuesAsync(ToolExecutionContext context, long characterID)
    {
        var values     = await characterRepository.GetCharacterStateValuesAsync(characterID);
        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID, StateScope.Category);
        var attrLookup = attributes.ToDictionary(a => a.ID);

        var result = new List<object>();

        foreach (var v in values)
        {
            var name = attrLookup.TryGetValue(v.AttributeID, out var attr) ? attr.Name : v.AttributeID.ToString();

            result.Add
            (
                new
                {
                    name,
                    value = v.Value
                }
            );
        }

        return result;
    }

    private async Task<List<object>> GetCharacterRelationsAsync(ToolExecutionContext context, long characterID)
    {
        var relations     = await characterRepository.GetRelationsByCharacterAsync(characterID);
        var allCharacters = await characterRepository.GetBySessionAsync(context.SessionID);
        var charLookup    = allCharacters.ToDictionary(c => c.ID);

        var result = new List<object>();

        foreach (var r in relations)
        {
            var otherID   = r.SourceCharacterID == characterID ? r.TargetCharacterID : r.SourceCharacterID;
            var otherName = charLookup.TryGetValue(otherID, out var other) ? other.Name : $"ID:{otherID}";
            var direction = r.SourceCharacterID == characterID ? "outgoing" : "incoming";

            result.Add
            (
                new
                {
                    target      = otherName,
                    type        = r.RelationType,
                    description = r.Description,
                    direction
                }
            );
        }

        return result;
    }

    private async Task<(StateAttribute? Attr, string? Error)> ResolveCategoryAttributeAsync
    (
        ToolExecutionContext context,
        string               attribute
    )
    {
        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID, StateScope.Category);
        var attr       = attributes.FirstOrDefault(a => a.Name == attribute);

        if (attr is null)
            return (null, JsonSerializer.Serialize(new { error = $"状态属性 {attribute} 不存在" }));

        return (attr, null);
    }
}
