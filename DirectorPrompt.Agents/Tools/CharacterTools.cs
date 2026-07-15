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
    ICharacterRepository       characterRepository,
    IStateRepository           stateRepository,
    ICharacterCategoryResolver categoryResolver,
    IEmbeddingServiceFactory   embeddingServiceFactory
)
{
    public IList<AIFunction> Create(ToolExecutionContext context) =>
    [
        AIFunctionFactory.Create
        (
            (string query, int? topK = null) => SearchCharacterAsync(context, query, topK),
            "search_character",
            """
            语义检索人物 (含已归档角色)
            query: 检索内容 (名字、别称或描述关键词)
            topK: 返回条数, 默认 5
            """
        ),
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
            (string name, string description, string categoryIDs, string reason, string? aliases = null) =>
                AddCharacterAsync(context, name, description, categoryIDs, aliases, reason),
            "add_character",
            """
            新增人物
            name: 人物名
            description: 描述
            categoryIDs: 分类 ID 列表 (逗号分隔)
            aliases: 别称列表 (逗号分隔, 可选)
            reason: 新增原因
            """
        ),
        AIFunctionFactory.Create
        (
            (string name, string description, string reason, string? categoryIDs = null) =>
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
            (string name, string alias) => AddAliasAsync(context, name, alias),
            "add_alias",
            """
            为人物添加别称
            name: 人物名
            alias: 别称
            """
        ),
        AIFunctionFactory.Create
        (
            (string sourceName, string targetName, string relationType, string reason, string? description = null, double? intensity = null) =>
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

    private async Task<string> SearchCharacterAsync
    (
        ToolExecutionContext context,
        string               query,
        int?                 topK
    )
    {
        Log.Information("工具调用: search_character(query={Query}, topK={TopK})", query, topK);

        var characters = await characterRepository.GetBySessionAsync(context.SessionID);

        if (characters.Count == 0)
        {
            Log.Information("工具调用完成: search_character, 结果=无人物");
            return JsonSerializer.Serialize(new { message = "无人物" });
        }

        var embeddingService = embeddingServiceFactory.Create(context.EmbeddingConfig);
        var fingerprint      = context.EmbeddingConfig.Fingerprint;

        var needsRegeneration = characters
                                .Where
                                (c =>
                                    {
                                        var text = BuildCharacterEmbeddingText(c);
                                        var hash = EmbeddingConversions.ComputeHash(text, fingerprint);
                                        return c.ContentHash != hash;
                                    }
                                )
                                .ToList();

        if (needsRegeneration.Count > 0)
        {
            Log.Information
            (
                "角色向量补全: 需生成 {Count}/{Total} 条",
                needsRegeneration.Count,
                characters.Count
            );

            var contents   = needsRegeneration.Select(BuildCharacterEmbeddingText).ToList();
            var embeddings = await embeddingService.GenerateEmbeddingsAsync(contents);

            for (var i = 0; i < needsRegeneration.Count; i++)
            {
                var c        = needsRegeneration[i];
                var text     = contents[i];
                var hash     = EmbeddingConversions.ComputeHash(text, fingerprint);
                var embBytes = EmbeddingConversions.FloatsToBytes(embeddings[i]);

                await characterRepository.SaveEmbeddingAsync(context.ProjectID, c.ID, embBytes, hash);
            }

            characters = await characterRepository.GetBySessionAsync(context.SessionID);
        }

        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query);
        var queryBytes     = EmbeddingConversions.FloatsToBytes(queryEmbedding);
        var limit          = topK ?? 5;

        var candidateIDs = characters.Select(c => c.ID).ToList();

        var searchResults = await characterRepository.SearchByVectorAsync
                            (
                                context.ProjectID,
                                queryBytes,
                                limit,
                                candidateIDs
                            );

        var charMap = characters.ToDictionary(c => c.ID);

        var result = searchResults
                     .Where(sr => charMap.ContainsKey(sr.characterID))
                     .Select
                     (sr =>
                         {
                             var c          = charMap[sr.characterID];
                             var similarity = 1f - sr.distance;

                             return new
                             {
                                 id          = c.ID,
                                 name        = c.Name,
                                 aliases     = c.Aliases,
                                 description = c.Description,
                                 status      = c.Status.ToString().ToLowerInvariant(),
                                 relevance   = Math.Round(similarity, 4)
                             };
                         }
                     )
                     .ToList();

        Log.Information("工具调用完成: search_character, 返回条目数={Count}", result.Count);

        return JsonSerializer.Serialize(result);
    }

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
                aliases     = character.Aliases,
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
                    aliases     = character.Aliases,
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
            var otherID = r.SourceCharacterID == character.ID ?
                              r.TargetCharacterID :
                              r.SourceCharacterID;
            var otherName = charLookup.TryGetValue(otherID, out var other) ?
                                other.Name :
                                $"ID:{otherID}";
            var direction = r.SourceCharacterID == character.ID ?
                                "outgoing" :
                                "incoming";

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
        string?              aliases,
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

        var aliasList = string.IsNullOrWhiteSpace(aliases) ?
                            [] :
                            aliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var character = new Character
        {
            ProjectID        = context.ProjectID,
            SessionID        = context.SessionID,
            Name             = name,
            Description      = description,
            Aliases          = aliasList,
            CategoryIDs      = categoryIDList,
            Status           = CharacterStatus.Active,
            LastTouchedRound = context.RoundID
        };

        var created = await characterRepository.CreateAsync(character, context.SessionID, context.RoundID);

        await categoryResolver.ResolveAndPersistAsync(created.ID, context.SessionID, context.RoundID);

        await GenerateAndSaveEmbeddingAsync(context, created);

        Log.Information("工具调用完成: add_character, characterID={ID}, name={Name}", created.ID, name);

        return JsonSerializer.Serialize(new { characterID = created.ID });
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

        await characterRepository.UpdateAsync(updated, context.SessionID, context.RoundID);

        if (!string.IsNullOrWhiteSpace(categoryIDs))
            await categoryResolver.ResolveAndPersistAsync(character.ID, context.SessionID, context.RoundID);

        await characterRepository.TouchAsync(character.ID, context.RoundID, context.SessionID);

        await GenerateAndSaveEmbeddingAsync(context, updated);

        return JsonSerializer.Serialize(new { name, success = true });
    }

    private async Task<string> AddAliasAsync
    (
        ToolExecutionContext context,
        string               name,
        string               alias
    )
    {
        Log.Information("工具调用: add_alias(name={Name}, alias={Alias})", name, alias);

        var character = await characterRepository.GetByNameAsync(context.SessionID, name);

        if (character is null)
            return JsonSerializer.Serialize(new { error = $"人物 {name} 不存在" });

        if (character.Aliases.Contains(alias))
            return JsonSerializer.Serialize(new { name, alias, success = true, message = "别称已存在" });

        await characterRepository.AddAliasAsync(character.ID, alias, context.SessionID, context.RoundID);

        await characterRepository.TouchAsync(character.ID, context.RoundID, context.SessionID);

        var refreshed = await characterRepository.GetByIDAsync(character.ID);

        if (refreshed is not null)
            await GenerateAndSaveEmbeddingAsync(context, refreshed);

        return JsonSerializer.Serialize(new { name, alias, success = true });
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

        float? intensityFloat = intensity is null ?
                                    null :
                                    (float)intensity.Value;

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
            context.SceneID ?? 0,
            context.RoundID
        );

        await characterRepository.TouchAsync(source.ID, context.RoundID, context.SessionID);
        await characterRepository.TouchAsync(target.ID, context.RoundID, context.SessionID);

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

        await characterRepository.EnterSceneAsync(character.ID, context.SceneID.Value, context.SessionID, context.RoundID);

        await characterRepository.TouchAsync(character.ID, context.RoundID, context.SessionID);

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

        await characterRepository.LeaveSceneAsync(character.ID, context.SceneID.Value, context.SessionID, context.RoundID);

        await characterRepository.TouchAsync(character.ID, context.RoundID, context.SessionID);

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

        if (attr.Driver == Driver.System || attr.ValueType == StateValueType.Enum)
            return JsonSerializer.Serialize(new { error = $"状态属性 {attribute} 为系统驱动或枚举类型, AI 不可直接修改" });

        var values       = await characterRepository.GetCharacterStateValuesAsync(character.ID);
        var currentValue = values.FirstOrDefault(v => v.AttributeID == attr.ID);
        var currentNum   = double.Parse(currentValue?.Value ?? "0", CultureInfo.InvariantCulture);
        var newValue     = currentNum + delta;

        await characterRepository.SetCharacterStateValueAsync
            (character.ID, attr.ID, newValue.ToString(CultureInfo.InvariantCulture), context.SessionID, context.RoundID);

        await characterRepository.TouchAsync(character.ID, context.RoundID, context.SessionID);

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

        if (attr.Driver == Driver.System || attr.ValueType == StateValueType.Enum)
            return JsonSerializer.Serialize(new { error = $"状态属性 {attribute} 为系统驱动或枚举类型, AI 不可直接修改" });

        await characterRepository.SetCharacterStateValueAsync(character.ID, attr.ID, value, context.SessionID, context.RoundID);

        await characterRepository.TouchAsync(character.ID, context.RoundID, context.SessionID);

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

    private async Task GenerateAndSaveEmbeddingAsync
    (
        ToolExecutionContext context,
        Character            character
    )
    {
        var text = BuildCharacterEmbeddingText(character);
        var hash = EmbeddingConversions.ComputeHash(text, context.EmbeddingConfig.Fingerprint);

        if (character.ContentHash == hash)
            return;

        var emb      = await embeddingServiceFactory.Create(context.EmbeddingConfig).GenerateEmbeddingAsync(text);
        var embBytes = EmbeddingConversions.FloatsToBytes(emb);

        await characterRepository.SaveEmbeddingAsync(context.ProjectID, character.ID, embBytes, hash);
    }

    private static string BuildCharacterEmbeddingText(Character character)
    {
        var parts = new List<string> { character.Name };

        if (character.Aliases.Length > 0)
            parts.Add(string.Join(", ", character.Aliases));

        if (!string.IsNullOrWhiteSpace(character.Description))
            parts.Add(character.Description);

        return string.Join("\n", parts);
    }

    private async Task<List<object>> GetCharacterStateValuesAsync(ToolExecutionContext context, long characterID)
    {
        var values     = await characterRepository.GetCharacterStateValuesAsync(characterID);
        var attributes = await stateRepository.GetAttributesAsync(context.ProjectID, StateScope.Category);
        var attrLookup = attributes.ToDictionary(a => a.ID);

        var result = new List<object>();

        foreach (var v in values)
        {
            var name = attrLookup.TryGetValue(v.AttributeID, out var attr) ?
                           attr.Name :
                           v.AttributeID.ToString();

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
            var otherID = r.SourceCharacterID == characterID ?
                              r.TargetCharacterID :
                              r.SourceCharacterID;
            var otherName = charLookup.TryGetValue(otherID, out var other) ?
                                other.Name :
                                $"ID:{otherID}";
            var direction = r.SourceCharacterID == characterID ?
                                "outgoing" :
                                "incoming";

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
