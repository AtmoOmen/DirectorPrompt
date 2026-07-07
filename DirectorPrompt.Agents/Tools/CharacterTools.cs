using System.Globalization;
using System.Text.Json;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Extensions.AI;
using Serilog;

namespace DirectorPrompt.Agents.Tools;

public sealed class CharacterTools
(
    ICharacterRepository characterRepository,
    IStateRepository     stateRepository
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
            (string name, string reason) => RemoveCharacterAsync(context, name, reason),
            "remove_character",
            """
            标记人物离场或死亡
            name: 人物名
            reason: 原因
            """
        ),
        AIFunctionFactory.Create
        (
            (string name, string description, string reason) =>
                UpdateCharacterAsync(context, name, description, reason),
            "update_character",
            """
            更新人物描述
            name: 人物名
            description: 新描述
            reason: 原因
            """
        ),
        AIFunctionFactory.Create
        (
            (string sourceName, string targetName, string relationType, string? description, string reason) =>
                SetRelationAsync(context, sourceName, targetName, relationType, description, reason),
            "set_relation",
            """
            设置或更新人物关系
            sourceName: 主体人物
            targetName: 客体人物
            relationType: 关系类型
            description: 关系描述 (可选)
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

        return JsonSerializer.Serialize
        (
            new
            {
                name        = character.Name,
                description = character.Description,
                categories  = character.CategoryIDs,
                status      = character.Status.ToString()
            }
        );
    }

    private async Task<string> GetSceneCharactersAsync(ToolExecutionContext context)
    {
        if (context.SceneID is null)
            return JsonSerializer.Serialize(Array.Empty<object>());

        var presence = await characterRepository.GetPresenceAsync(context.SceneID.Value);
        var result   = new List<object>();

        foreach (var p in presence)
        {
            var character = await characterRepository.GetByIDAsync(p.CharacterID);

            if (character is not null)
            {
                result.Add
                (
                    new
                    {
                        name        = character.Name,
                        description = character.Description,
                        status      = character.Status.ToString()
                    }
                );
            }
        }

        return JsonSerializer.Serialize(result);
    }

    private async Task<string> GetRelationsAsync(ToolExecutionContext context, string characterName)
    {
        var character = await characterRepository.GetByNameAsync(context.SessionID, characterName);

        if (character is null)
            return JsonSerializer.Serialize(new { error = $"人物 {characterName} 不存在" });

        var relations = await characterRepository.GetRelationsByCharacterAsync(character.ID);
        var result = relations.Select
        (r => new
            {
                target = r.TargetCharacterID == character.ID ?
                             r.SourceCharacterID :
                             r.TargetCharacterID,
                type        = r.RelationType,
                description = r.Description,
                direction = r.SourceCharacterID == character.ID ?
                                "outgoing" :
                                "incoming"
            }
        );

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

        Log.Information("工具调用完成: add_character, characterID={ID}, name={Name}", created.ID, name);

        return JsonSerializer.Serialize(new { characterID = created.ID });
    }

    private async Task<string> RemoveCharacterAsync(ToolExecutionContext context, string name, string reason)
    {
        Log.Information("工具调用: remove_character(name={Name}, reason={Reason})", name, reason);
        var character = await characterRepository.GetByNameAsync(context.SessionID, name);

        if (character is null)
            return JsonSerializer.Serialize(new { error = $"人物 {name} 不存在" });

        await characterRepository.SetStatusAsync(character.ID, CharacterStatus.Left);

        return JsonSerializer.Serialize(new { name, status = "left" });
    }

    private async Task<string> UpdateCharacterAsync
    (
        ToolExecutionContext context,
        string               name,
        string               description,
        string               reason
    )
    {
        var character = await characterRepository.GetByNameAsync(context.SessionID, name);

        if (character is null)
            return JsonSerializer.Serialize(new { error = $"人物 {name} 不存在" });

        var updated = character with { Description = description };
        await characterRepository.UpdateAsync(updated);

        return JsonSerializer.Serialize(new { name, success = true });
    }

    private async Task<string> SetRelationAsync
    (
        ToolExecutionContext context,
        string               sourceName,
        string               targetName,
        string               relationType,
        string?              description,
        string               reason
    )
    {
        var source = await characterRepository.GetByNameAsync(context.SessionID, sourceName);
        var target = await characterRepository.GetByNameAsync(context.SessionID, targetName);

        if (source is null)
            return JsonSerializer.Serialize(new { error = $"人物 {sourceName} 不存在" });

        if (target is null)
            return JsonSerializer.Serialize(new { error = $"人物 {targetName} 不存在" });

        await characterRepository.SetRelationAsync
        (
            context.SessionID,
            source.ID,
            target.ID,
            relationType,
            description,
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

        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID, StateScope.Category);
        var attr       = attributes.FirstOrDefault(a => a.Name == attribute);

        if (attr is null)
            return JsonSerializer.Serialize(new { error = $"状态属性 {attribute} 不存在" });

        var values       = await characterRepository.GetCharacterStateValuesAsync(character.ID);
        var currentValue = values.FirstOrDefault(v => v.AttributeID == attr.ID);
        var currentNum   = double.Parse(currentValue?.Value ?? "0");
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

        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID, StateScope.Category);
        var attr       = attributes.FirstOrDefault(a => a.Name == attribute);

        if (attr is null)
            return JsonSerializer.Serialize(new { error = $"状态属性 {attribute} 不存在" });

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
}
