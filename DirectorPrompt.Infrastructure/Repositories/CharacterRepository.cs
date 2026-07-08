using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class CharacterRepository : ICharacterRepository
{
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly IRoundChangeRepository  roundChangeRepository;

    public CharacterRepository(SqliteConnectionFactory connectionFactory, IRoundChangeRepository roundChangeRepository)
    {
        this.connectionFactory     = connectionFactory;
        this.roundChangeRepository = roundChangeRepository;
    }

    private static List<DirectiveConfig> ParseDirectiveConfigs(string json)
    {
        var result = new List<DirectiveConfig>();

        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return result;

        try
        {
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var typeStr = item.TryGetProperty("type", out var t) && t.ValueKind != JsonValueKind.Null ?
                                  t.GetString() ?? "Plot" :
                                  "Plot";

                var type = typeStr switch
                {
                    "Tone"                => DirectiveType.Tone,
                    "TemporaryConstraint" => DirectiveType.TemporaryConstraint,
                    "SceneChange"         => DirectiveType.SceneChange,
                    _                     => DirectiveType.Plot
                };

                var content = item.TryGetProperty("content", out var c) && c.ValueKind != JsonValueKind.Null ?
                                  c.GetString() ?? string.Empty :
                                  string.Empty;

                var ttl = item.TryGetProperty("ttl", out var ttlEl) && ttlEl.ValueKind == JsonValueKind.Number ?
                              ttlEl.GetInt32() :
                              (int?)null;

                result.Add
                (
                    new DirectiveConfig
                    {
                        Type    = type,
                        Content = content,
                        TTL     = ttl
                    }
                );
            }
        }
        catch
        {
            // ignored
        }

        return result;
    }

    public async Task<Character?> GetByIDAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<CharacterRow>
                  (
                      "SELECT * FROM characters WHERE id = @id",
                      new { id }
                  );

        return row?.ToCharacter();
    }

    public async Task<Character?> GetByNameAsync(long sessionID, string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<CharacterRow>
                  (
                      "SELECT * FROM characters WHERE session_id = @sessionID AND name = @name",
                      new { sessionID, name }
                  );

        return row?.ToCharacter();
    }

    public async Task<IReadOnlyList<Character>> GetBySessionAsync(long sessionID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<CharacterRow>
                   (
                       "SELECT * FROM characters WHERE session_id = @sessionID ORDER BY id",
                       new { sessionID }
                   );

        return rows.Select(r => r.ToCharacter()).ToList();
    }

    public async Task<IReadOnlyList<Character>> GetByProjectAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<CharacterRow>
                   (
                       "SELECT * FROM characters WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.Select(r => r.ToCharacter()).ToList();
    }

    public async Task<IReadOnlyList<Character>> GetBySceneAsync(long sceneID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<CharacterRow>
                   (
                       """
                       SELECT c.* FROM characters c
                       JOIN character_scene_presence p ON p.character_id = c.id
                       WHERE p.scene_id = @sceneID
                         AND c.status = 'active'
                       ORDER BY c.id
                       """,
                       new { sceneID }
                   );

        return rows.Select(r => r.ToCharacter()).ToList();
    }

    public async Task<Character> CreateAsync(Character character, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var now = DateTime.UtcNow.ToString("O");

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO characters (project_id, session_id, name, description, category_ids, status, enter_directives, exit_directives, created_at, updated_at)
                     VALUES (@projectID, @sessionID, @name, @description, @categoryIDs, @status, @enterDirectives, @exitDirectives, @createdAt, @updatedAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID       = character.ProjectID,
                         sessionID       = character.SessionID,
                         name            = character.Name,
                         description     = character.Description,
                         categoryIDs     = JsonHelper.Serialize(character.CategoryIDs),
                         status          = character.Status.ToString().ToLowerInvariant(),
                         enterDirectives = JsonHelper.Serialize(character.EnterDirectives),
                         exitDirectives  = JsonHelper.Serialize(character.ExitDirectives),
                         createdAt       = now,
                         updatedAt       = now
                     }
                 );

        await roundChangeRepository.RecordCreateAsync(RoundContext.Current ?? 0, "characters", id, cancellationToken: cancellationToken);

        return character with { ID = id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
    }

    public async Task UpdateAsync(Character character, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var oldRow = await connection.QueryFirstOrDefaultAsync<IDictionary<string, object>>
                     (
                         "SELECT * FROM characters WHERE id = @id",
                         new { id = character.ID }
                     );

        await connection.ExecuteAsync
        (
            """
            UPDATE characters
            SET name = @name,
                description = @description,
                category_ids = @categoryIDs,
                status = @status,
                enter_directives = @enterDirectives,
                exit_directives = @exitDirectives,
                updated_at = @updatedAt
            WHERE id = @id
            """,
            new
            {
                id              = character.ID,
                name            = character.Name,
                description     = character.Description,
                categoryIDs     = JsonHelper.Serialize(character.CategoryIDs),
                status          = character.Status.ToString().ToLowerInvariant(),
                enterDirectives = JsonHelper.Serialize(character.EnterDirectives),
                exitDirectives  = JsonHelper.Serialize(character.ExitDirectives),
                updatedAt       = DateTime.UtcNow.ToString("O")
            }
        );

        if (oldRow is not null)
            await roundChangeRepository.RecordUpdateAsync
                (RoundContext.Current ?? 0, "characters", character.ID, JsonSerializer.Serialize(oldRow), cancellationToken);
    }

    public async Task SetStatusAsync(long characterID, CharacterStatus status, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var oldRow = await connection.QueryFirstOrDefaultAsync<IDictionary<string, object>>
                     (
                         "SELECT * FROM characters WHERE id = @id",
                         new { id = characterID }
                     );

        await connection.ExecuteAsync
        (
            "UPDATE characters SET status = @status, updated_at = @updatedAt WHERE id = @id",
            new
            {
                id        = characterID,
                status    = status.ToString().ToLowerInvariant(),
                updatedAt = DateTime.UtcNow.ToString("O")
            }
        );

        if (oldRow is not null)
            await roundChangeRepository.RecordUpdateAsync(RoundContext.Current ?? 0, "characters", characterID, JsonSerializer.Serialize(oldRow), cancellationToken);
    }

    public async Task UpdateDirectivesAsync
    (
        long                           characterID,
        IReadOnlyList<DirectiveConfig> enterDirectives,
        IReadOnlyList<DirectiveConfig> exitDirectives,
        CancellationToken              cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            """
            UPDATE characters
            SET enter_directives = @enterDirectives,
                exit_directives = @exitDirectives,
                updated_at = @updatedAt
            WHERE id = @id
            """,
            new
            {
                id              = characterID,
                enterDirectives = JsonHelper.Serialize(enterDirectives),
                exitDirectives  = JsonHelper.Serialize(exitDirectives),
                updatedAt       = DateTime.UtcNow.ToString("O")
            }
        );
    }

    public async Task<IReadOnlyList<CharacterCategory>> GetCategoriesAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<CharacterCategoryRow>
                   (
                       "SELECT * FROM character_categories WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.Select(r => r.ToCharacterCategory()).ToList();
    }

    public async Task<CharacterCategory> CreateCategoryAsync(CharacterCategory category, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO character_categories (project_id, name, description, parent_category_ids)
                     VALUES (@projectID, @name, @description, @parentCategoryIDs);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID         = category.ProjectID,
                         name              = category.Name,
                         description       = category.Description,
                         parentCategoryIDs = JsonHelper.Serialize(category.ParentCategoryIDs)
                     }
                 );

        return category with { ID = id };
    }

    public async Task UpdateCategoryAsync(CharacterCategory category, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            """
            UPDATE character_categories
            SET name = @name, description = @description, parent_category_ids = @parentCategoryIDs
            WHERE id = @id
            """,
            new
            {
                id                = category.ID,
                name              = category.Name,
                description       = category.Description,
                parentCategoryIDs = JsonHelper.Serialize(category.ParentCategoryIDs)
            }
        );
    }

    public async Task DeleteCategoryAsync(long categoryID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            "DELETE FROM character_categories WHERE id = @categoryID",
            new { categoryID }
        );
    }

    public async Task<IReadOnlyList<Character>> GetCharactersByCategoryAsync
    (
        long              projectID,
        long              categoryID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<CharacterRow>
                   (
                       """
                       SELECT * FROM characters
                       WHERE project_id = @projectID
                         AND EXISTS (
                           SELECT 1 FROM json_each(category_ids) WHERE value = @categoryID
                         )
                       ORDER BY id
                       """,
                       new { projectID, categoryID }
                   );

        return rows.Select(r => r.ToCharacter()).ToList();
    }

    public async Task<IReadOnlyList<CharacterRelation>> GetRelationsAsync(long sessionID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<CharacterRelationRow>
                   (
                       "SELECT * FROM character_relations WHERE session_id = @sessionID ORDER BY id",
                       new { sessionID }
                   );

        return rows.Select(r => r.ToCharacterRelation()).ToList();
    }

    public async Task<IReadOnlyList<CharacterRelation>> GetRelationsByCharacterAsync(long characterID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<CharacterRelationRow>
                   (
                       "SELECT * FROM character_relations WHERE source_character_id = @characterID OR target_character_id = @characterID ORDER BY id",
                       new { characterID }
                   );

        return rows.Select(r => r.ToCharacterRelation()).ToList();
    }

    public async Task<CharacterRelation> SetRelationAsync
    (
        long                 sessionID,
        long                 sourceCharacterID,
        long                 targetCharacterID,
        string               relationType,
        string?              description,
        float?               intensity,
        RelationChangeSource source,
        string               reason,
        long                 sceneID,
        CancellationToken    cancellationToken = default
    )
    {
        await using var connection  = await connectionFactory.CreateAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow.ToString("O");

        var existing = await connection.QueryFirstOrDefaultAsync<CharacterRelationRow>
                       (
                           """
                           SELECT * FROM character_relations
                           WHERE session_id = @sessionID
                             AND source_character_id = @sourceID
                             AND target_character_id = @targetID
                           """,
                           new { sessionID, sourceID = sourceCharacterID, targetID = targetCharacterID },
                           transaction
                       );

        long relationID;
        long projectID;

        if (existing is not null)
        {
            projectID = existing.Project_ID;
            await connection.ExecuteAsync
            (
                """
                UPDATE character_relations
                SET relation_type = @relationType,
                    description = @description,
                    intensity = @intensity,
                    updated_at = @updatedAt
                WHERE id = @id
                """,
                new
                {
                    id = existing.ID,
                    relationType,
                    description,
                    intensity,
                    updatedAt = now
                },
                transaction
            );

            relationID = existing.ID;

            await connection.ExecuteAsync
            (
                """
                INSERT INTO character_relation_logs
                    (relation_id, old_type, new_type, old_description, new_description, source, reason, scene_id, created_at)
                VALUES (@relationID, @oldType, @newType, @oldDescription, @newDescription, @source, @reason, @sceneID, @createdAt)
                """,
                new
                {
                    relationID,
                    oldType        = existing.Relation_Type,
                    newType        = relationType,
                    oldDescription = existing.Description,
                    newDescription = description,
                    source         = source.ToString().ToLowerInvariant(),
                    reason,
                    sceneID,
                    createdAt = now
                },
                transaction
            );
        }
        else
        {
            projectID = await connection.QueryFirstAsync<long>
                        (
                            "SELECT project_id FROM characters WHERE id = @sourceID",
                            new { sourceID = sourceCharacterID },
                            transaction
                        );

            relationID = await connection.ExecuteScalarAsync<long>
                         (
                             """
                             INSERT INTO character_relations
                                 (project_id, session_id, source_character_id, target_character_id, relation_type, description, intensity, created_at, updated_at)
                             VALUES (@projectID, @sessionID, @sourceID, @targetID, @relationType, @description, @intensity, @createdAt, @updatedAt);
                             SELECT last_insert_rowid();
                             """,
                             new
                             {
                                 projectID,
                                 sessionID,
                                 sourceID = sourceCharacterID,
                                 targetID = targetCharacterID,
                                 relationType,
                                 description,
                                 intensity,
                                 createdAt = now,
                                 updatedAt = now
                             },
                             transaction
                         );

            await connection.ExecuteAsync
            (
                """
                INSERT INTO character_relation_logs
                    (relation_id, old_type, new_type, old_description, new_description, source, reason, scene_id, created_at)
                VALUES (@relationID, NULL, @newType, NULL, @newDescription, @source, @reason, @sceneID, @createdAt)
                """,
                new
                {
                    relationID,
                    newType        = relationType,
                    newDescription = description,
                    source         = source.ToString().ToLowerInvariant(),
                    reason,
                    sceneID,
                    createdAt = now
                },
                transaction
            );
        }

        await transaction.CommitAsync(cancellationToken);

        var roundID = RoundContext.Current ?? 0;

        if (existing is not null)
        {
            var oldData = JsonSerializer.Serialize(existing);
            await roundChangeRepository.RecordUpdateAsync(roundID, "character_relations", relationID, oldData, cancellationToken);
        }
        else
            await roundChangeRepository.RecordCreateAsync(roundID, "character_relations", relationID, cancellationToken: cancellationToken);

        return new CharacterRelation
        {
            ID                = relationID,
            ProjectID         = projectID,
            SessionID         = sessionID,
            SourceCharacterID = sourceCharacterID,
            TargetCharacterID = targetCharacterID,
            RelationType      = relationType,
            Description       = description,
            Intensity         = intensity,
            CreatedAt         = DateTime.UtcNow,
            UpdatedAt         = DateTime.UtcNow
        };
    }

    public async Task<IReadOnlyList<CharacterRelationLog>> GetRelationLogsAsync
    (
        long              relationID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<CharacterRelationLogRow>
                   (
                       "SELECT * FROM character_relation_logs WHERE relation_id = @relationID ORDER BY id",
                       new { relationID }
                   );

        return rows.Select(r => r.ToCharacterRelationLog()).ToList();
    }

    public async Task<IReadOnlyList<CharacterScenePresence>> GetPresenceAsync(long sceneID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync
                   (
                       "SELECT character_id AS CharacterID, scene_id AS SceneID FROM character_scene_presence WHERE scene_id = @sceneID",
                       new { sceneID }
                   );

        return rows.Select
        (r => new CharacterScenePresence
            {
                CharacterID = (long)r.CharacterID,
                SceneID     = (long)r.SceneID
            }
        ).ToList();
    }

    public async Task EnterSceneAsync(long characterID, long sceneID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            """
            INSERT OR IGNORE INTO character_scene_presence (character_id, scene_id)
            VALUES (@characterID, @sceneID)
            """,
            new { characterID, sceneID }
        );

        var rowData = JsonSerializer.Serialize(new { character_id = characterID, scene_id = sceneID });
        await roundChangeRepository.RecordCreateAsync(RoundContext.Current ?? 0, "character_scene_presence", 0, rowData, cancellationToken);
    }

    public async Task LeaveSceneAsync(long characterID, long sceneID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var oldData = JsonSerializer.Serialize(new { character_id = characterID, scene_id = sceneID });

        await connection.ExecuteAsync
        (
            "DELETE FROM character_scene_presence WHERE character_id = @characterID AND scene_id = @sceneID",
            new { characterID, sceneID }
        );

        await roundChangeRepository.RecordDeleteAsync(RoundContext.Current ?? 0, "character_scene_presence", 0, oldData, cancellationToken);
    }

    public async Task<CharacterCategoryResolution?> GetResolvedCategoriesAsync(long characterID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync
                  (
                      "SELECT category_ids, attribute_ids FROM character_category_resolutions WHERE character_id = @characterID",
                      new { characterID }
                  );

        if (row is null)
            return null;

        return new CharacterCategoryResolution
        {
            CharacterID  = characterID,
            CategoryIDs  = JsonHelper.DeserializeInt64Array((string)row.category_ids),
            AttributeIDs = JsonHelper.DeserializeInt64Array((string)row.attribute_ids)
        };
    }

    public async Task UpdateResolvedCategoriesAsync(CharacterCategoryResolution resolved, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            """
            INSERT INTO character_category_resolutions (character_id, category_ids, attribute_ids)
            VALUES (@characterID, @categoryIDs, @attributeIDs)
            ON CONFLICT(character_id)
            DO UPDATE SET category_ids = @categoryIDs, attribute_ids = @attributeIDs
            """,
            new
            {
                characterID  = resolved.CharacterID,
                categoryIDs  = JsonHelper.Serialize(resolved.CategoryIDs),
                attributeIDs = JsonHelper.Serialize(resolved.AttributeIDs)
            }
        );
    }

    public async Task<IReadOnlyList<CharacterStateValue>> GetCharacterStateValuesAsync(long characterID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<CharacterStateValueRow>
                   (
                       "SELECT * FROM character_state_values WHERE character_id = @characterID",
                       new { characterID }
                   );

        return rows.Select
        (r => new CharacterStateValue
            {
                CharacterID = r.Character_ID,
                AttributeID = r.Attribute_ID,
                Value       = r.Value,
                UpdatedAt   = DateTime.Parse(r.Updated_At)
            }
        ).ToList();
    }

    public async Task SetCharacterStateValueAsync
    (
        long              characterID,
        long              attributeID,
        string            value,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var oldRow = await connection.QueryFirstOrDefaultAsync<IDictionary<string, object>>
                     (
                         "SELECT * FROM character_state_values WHERE character_id = @characterID AND attribute_id = @attributeID",
                         new { characterID, attributeID }
                     );

        await connection.ExecuteAsync
        (
            """
            INSERT INTO character_state_values (character_id, attribute_id, value, updated_at)
            VALUES (@characterID, @attributeID, @value, @updatedAt)
            ON CONFLICT(character_id, attribute_id)
            DO UPDATE SET value = @value, updated_at = @updatedAt
            """,
            new
            {
                characterID,
                attributeID,
                value,
                updatedAt = DateTime.UtcNow.ToString("O")
            }
        );

        var roundID = RoundContext.Current ?? 0;

        if (oldRow is null)
        {
            var rowData = JsonSerializer.Serialize
            (
                new
                {
                    character_id = characterID,
                    attribute_id = attributeID,
                    value,
                    updated_at = DateTime.UtcNow.ToString("O")
                }
            );
            await roundChangeRepository.RecordCreateAsync(roundID, "character_state_values", 0, rowData, cancellationToken);
        }
        else
            await roundChangeRepository.RecordUpdateAsync(roundID, "character_state_values", 0, JsonSerializer.Serialize(oldRow), cancellationToken);
    }

    public async Task CloneProjectCharactersToSessionAsync
    (
        long              projectID,
        long              sessionID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var now = DateTime.UtcNow.ToString("O");

        await connection.ExecuteAsync
        (
            """
            INSERT INTO characters (project_id, session_id, name, description, category_ids, status, enter_directives, exit_directives, created_at, updated_at)
            SELECT project_id, @sessionID, name, description, category_ids, status, enter_directives, exit_directives, @now, @now
            FROM characters
            WHERE project_id = @projectID AND session_id IS NULL
            """,
            new { projectID, sessionID, now }
        );
    }

    private sealed class CharacterRow
    {
        public long   ID               { get; set; }
        public long   Project_ID       { get; set; }
        public long?  Session_ID       { get; set; }
        public string Name             { get; set; } = string.Empty;
        public string Description      { get; set; } = string.Empty;
        public string Category_IDs     { get; set; } = "[]";
        public string Status           { get; set; } = "active";
        public string Enter_Directives { get; set; } = "[]";
        public string Exit_Directives  { get; set; } = "[]";
        public string Created_At       { get; set; } = string.Empty;
        public string Updated_At       { get; set; } = string.Empty;

        public Character ToCharacter() =>
            new()
            {
                ID          = ID,
                ProjectID   = Project_ID,
                SessionID   = Session_ID ?? 0,
                Name        = Name,
                Description = Description,
                CategoryIDs = JsonHelper.DeserializeInt64Array(Category_IDs),
                Status = Status switch
                {
                    "left" => CharacterStatus.Left,
                    "dead" => CharacterStatus.Dead,
                    _      => CharacterStatus.Active
                },
                EnterDirectives = ParseDirectiveConfigs(Enter_Directives),
                ExitDirectives  = ParseDirectiveConfigs(Exit_Directives),
                CreatedAt       = DateTime.Parse(Created_At),
                UpdatedAt       = DateTime.Parse(Updated_At)
            };
    }

    private sealed class CharacterCategoryRow
    {
        public long    ID                  { get; set; }
        public long    Project_ID          { get; set; }
        public string  Name                { get; set; } = string.Empty;
        public string? Description         { get; set; }
        public string  Parent_Category_IDs { get; set; } = "[]";

        public CharacterCategory ToCharacterCategory() =>
            new()
            {
                ID                = ID,
                ProjectID         = Project_ID,
                Name              = Name,
                Description       = Description,
                ParentCategoryIDs = JsonHelper.DeserializeInt64Array(Parent_Category_IDs)
            };
    }

    private sealed class CharacterRelationRow
    {
        public long    ID                  { get; set; }
        public long    Project_ID          { get; set; }
        public long?   Session_ID          { get; set; }
        public long    Source_Character_ID { get; set; }
        public long    Target_Character_ID { get; set; }
        public string  Relation_Type       { get; set; } = string.Empty;
        public string? Description         { get; set; }
        public float?  Intensity           { get; set; }
        public string  Created_At          { get; set; } = string.Empty;
        public string  Updated_At          { get; set; } = string.Empty;

        public CharacterRelation ToCharacterRelation() =>
            new()
            {
                ID                = ID,
                ProjectID         = Project_ID,
                SessionID         = Session_ID ?? 0,
                SourceCharacterID = Source_Character_ID,
                TargetCharacterID = Target_Character_ID,
                RelationType      = Relation_Type,
                Description       = Description,
                Intensity         = Intensity,
                CreatedAt         = DateTime.Parse(Created_At),
                UpdatedAt         = DateTime.Parse(Updated_At)
            };
    }

    private sealed class CharacterStateValueRow
    {
        public long   Character_ID { get; set; }
        public long   Attribute_ID { get; set; }
        public string Value        { get; set; } = string.Empty;
        public string Updated_At   { get; set; } = string.Empty;
    }

    private sealed class CharacterRelationLogRow
    {
        public long    ID              { get; set; }
        public long    Relation_ID     { get; set; }
        public string? Old_Type        { get; set; }
        public string  New_Type        { get; set; } = string.Empty;
        public string? Old_Description { get; set; }
        public string? New_Description { get; set; }
        public string  Source          { get; set; } = string.Empty;
        public string  Reason          { get; set; } = string.Empty;
        public long    Scene_ID        { get; set; }
        public string  Created_At      { get; set; } = string.Empty;

        public CharacterRelationLog ToCharacterRelationLog() =>
            new()
            {
                ID             = ID,
                RelationID     = Relation_ID,
                OldType        = Old_Type,
                NewType        = New_Type,
                OldDescription = Old_Description,
                NewDescription = New_Description,
                Source = Source switch
                {
                    "director_manual" => RelationChangeSource.DirectorManual,
                    _                 => RelationChangeSource.MemorySubAgent
                },
                Reason    = Reason,
                SceneID   = Scene_ID,
                CreatedAt = DateTime.Parse(Created_At)
            };
    }
}
