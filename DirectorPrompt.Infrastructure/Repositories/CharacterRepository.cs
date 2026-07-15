using System.Data.Common;
using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class CharacterRepository
(
    SQLiteConnectionFactory connectionFactory,
    VectorTableManager      vectorTableManager,
    SQLiteDatabaseScheduler scheduler
)
    : ICharacterRepository
{
    public async Task<Character?> GetByIDAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<Character>
               (
                   "SELECT * FROM characters WHERE id = @id",
                   new { id }
               );
    }

    public async Task<Character?> GetByNameAsync(long sessionID, string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<Character>
               (
                   "SELECT * FROM characters WHERE session_id = @sessionID AND name = @name",
                   new { sessionID, name }
               );
    }

    public async Task<IReadOnlyList<Character>> GetBySessionAsync(long sessionID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<Character>
                   (
                       "SELECT * FROM characters WHERE session_id = @sessionID ORDER BY id",
                       new { sessionID }
                   );

        return rows.ToList();
    }

    public async Task<IReadOnlyList<Character>> GetActiveBySessionAsync(long sessionID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<Character>
                   (
                       "SELECT * FROM characters WHERE session_id = @sessionID AND status = 'Active' ORDER BY last_touched_round DESC, id",
                       new { sessionID }
                   );

        return rows.ToList();
    }

    public async Task<IReadOnlyList<Character>> GetBySceneAsync(long sceneID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<Character>
                   (
                       """
                       SELECT c.* FROM characters c
                       JOIN character_scene_presence p ON p.character_id = c.id
                       WHERE p.scene_id = @sceneID
                         AND c.status = 'Active'
                       ORDER BY c.id
                       """,
                       new { sceneID }
                   );

        return rows.ToList();
    }

    public async Task<IReadOnlyList<Character>> GetByIDsAsync
    (
        long                sessionID,
        IReadOnlyList<long> characterIDs,
        CancellationToken   cancellationToken = default
    )
    {
        if (characterIDs.Count == 0)
            return [];

        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<Character>
                   (
                       new CommandDefinition
                       (
                           "SELECT * FROM characters WHERE session_id = @sessionID AND id IN @characterIDs",
                           new { sessionID, characterIDs },
                           cancellationToken: cancellationToken
                       )
                   );

        return rows.ToList();
    }

    public async Task<CharacterPage> GetPageAsync(CharacterPageQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var searchPattern = string.IsNullOrWhiteSpace(query.SearchText) ?
                                null :
                                $"%{query.SearchText.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
        var rows = await connection.QueryAsync<Character>
                   (
                       new CommandDefinition
                       (
                           """
                           SELECT c.*
                           FROM characters c
                           WHERE c.session_id = @SessionID
                             AND c.status = 'Active'
                             AND (@AfterID IS NULL OR c.id > @AfterID)
                             AND
                             (
                                 @CategoryID IS NULL
                                 OR EXISTS
                                 (
                                     SELECT 1
                                     FROM json_each(c.category_ids)
                                     WHERE value = @CategoryID
                                 )
                             )
                             AND
                             (
                                 @SearchPattern IS NULL
                                 OR c.name LIKE @SearchPattern ESCAPE '\'
                                 OR c.description LIKE @SearchPattern ESCAPE '\'
                             )
                           ORDER BY c.id
                           LIMIT @Take
                           """,
                           new
                           {
                               query.SessionID,
                               query.AfterID,
                               query.CategoryID,
                               SearchPattern = searchPattern,
                               Take          = pageSize + 1
                           },
                           cancellationToken: cancellationToken
                       )
                   );

        var items   = rows.ToList();
        var hasMore = items.Count > pageSize;

        if (hasMore)
            items.RemoveAt(items.Count - 1);

        return new CharacterPage
        (
            items,
            hasMore ?
                items[^1].ID :
                null
        );
    }

    public Task<Character> CreateAsync(Character character, long sessionID, long roundID, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var             now         = DateTime.UtcNow;
                var id = await connection.ExecuteScalarAsync<long>
                         (
                             new CommandDefinition
                             (
                                 """
                                 INSERT INTO characters (project_id, session_id, name, description, aliases, category_ids, status, touch_count, last_touched_round, created_at, updated_at)
                                 VALUES (@projectID, @sessionID, @name, @description, @aliases, @categoryIDs, @status, @touchCount, @lastTouchedRound, @createdAt, @updatedAt);
                                 SELECT last_insert_rowid();
                                 """,
                                 new
                                 {
                                     projectID        = character.ProjectID,
                                     sessionID        = character.SessionID,
                                     name             = character.Name,
                                     description      = character.Description,
                                     aliases          = character.Aliases,
                                     categoryIDs      = character.CategoryIDs,
                                     status           = character.Status,
                                     touchCount       = character.TouchCount,
                                     lastTouchedRound = character.LastTouchedRound,
                                     createdAt        = now,
                                     updatedAt        = now
                                 },
                                 transaction,
                                 cancellationToken: token
                             )
                         );
                await RoundChangeRepository.RecordAsync
                (
                    connection,
                    transaction,
                    sessionID,
                    roundID,
                    "characters",
                    id,
                    "create",
                    null,
                    token
                );
                await transaction.CommitAsync(token);

                return character with { ID = id, CreatedAt = now, UpdatedAt = now };
            },
            cancellationToken: cancellationToken
        );

    public Task UpdateAsync(Character character, long sessionID, long roundID, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var oldRow = await RowReader.ReadRowAsync
                             (
                                 connection,
                                 "SELECT * FROM characters WHERE id = @id",
                                 new { id = character.ID },
                                 transaction,
                                 token
                             );
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        """
                        UPDATE characters
                        SET name = @name,
                            description = @description,
                            aliases = @aliases,
                            category_ids = @categoryIDs,
                            status = @status,
                            updated_at = @updatedAt
                        WHERE id = @id
                        """,
                        new
                        {
                            id          = character.ID,
                            name        = character.Name,
                            description = character.Description,
                            aliases     = character.Aliases,
                            categoryIDs = character.CategoryIDs,
                            status      = character.Status,
                            updatedAt   = DateTime.UtcNow
                        },
                        transaction,
                        cancellationToken: token
                    )
                );

                if (oldRow is not null)
                {
                    await RoundChangeRepository.RecordAsync
                    (
                        connection,
                        transaction,
                        sessionID,
                        roundID,
                        "characters",
                        character.ID,
                        "update",
                        JsonSerializer.Serialize(oldRow, JsonOptions.Compact),
                        token
                    );
                }

                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public Task TouchAsync(long characterID, long roundID, long sessionID, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var oldRow = await RowReader.ReadRowAsync
                             (
                                 connection,
                                 "SELECT * FROM characters WHERE id = @characterID",
                                 new { characterID },
                                 transaction,
                                 token
                             );
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        """
                        UPDATE characters
                        SET touch_count = touch_count + 1,
                            last_touched_round = @roundID,
                            status = 'Active',
                            updated_at = @updatedAt
                        WHERE id = @characterID
                        """,
                        new { characterID, roundID, updatedAt = DateTime.UtcNow },
                        transaction,
                        cancellationToken: token
                    )
                );

                if (oldRow is not null)
                {
                    await RoundChangeRepository.RecordAsync
                    (
                        connection,
                        transaction,
                        sessionID,
                        roundID,
                        "characters",
                        characterID,
                        "update",
                        JsonSerializer.Serialize(oldRow, JsonOptions.Compact),
                        token
                    );
                }

                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public Task ArchiveAsync(long characterID, long sessionID, long roundID, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                await ArchiveAsync(connection, transaction, characterID, sessionID, roundID, token);
                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public Task ArchiveStaleAsync
    (
        long              sessionID,
        long              currentRound,
        int               threshold,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var staleIDs = await connection.QueryAsync<long>
                               (
                                   new CommandDefinition
                                   (
                                       """
                                       SELECT id FROM characters
                                       WHERE session_id = @sessionID
                                         AND status = 'Active'
                                         AND (@currentRound - last_touched_round) > @threshold
                                       """,
                                       new { sessionID, currentRound, threshold },
                                       transaction,
                                       cancellationToken: token
                                   )
                               );

                foreach (var id in staleIDs)
                    await ArchiveAsync(connection, transaction, id, sessionID, currentRound, token);

                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public Task AddAliasAsync(long characterID, string alias, long sessionID, long roundID, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var oldRow = await RowReader.ReadRowAsync
                             (
                                 connection,
                                 "SELECT * FROM characters WHERE id = @characterID",
                                 new { characterID },
                                 transaction,
                                 token
                             );

                if (oldRow is null)
                    return;

                var aliasesJSON = (string)oldRow["aliases"]!;
                var aliases     = JsonSerializer.Deserialize<string[]>(aliasesJSON, JsonOptions.Compact) ?? [];

                if (aliases.Contains(alias))
                    return;

                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        "UPDATE characters SET aliases = @aliases, updated_at = @updatedAt WHERE id = @characterID",
                        new
                        {
                            characterID,
                            aliases   = aliases.Append(alias).ToArray(),
                            updatedAt = DateTime.UtcNow
                        },
                        transaction,
                        cancellationToken: token
                    )
                );
                await RoundChangeRepository.RecordAsync
                (
                    connection,
                    transaction,
                    sessionID,
                    roundID,
                    "characters",
                    characterID,
                    "update",
                    JsonSerializer.Serialize(oldRow, JsonOptions.Compact),
                    token
                );
                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public async Task SaveEmbeddingAsync
    (
        long              projectID,
        long              characterID,
        byte[]            embedding,
        string            contentHash,
        CancellationToken cancellationToken = default
    )
    {
        var dimension = embedding.Length / sizeof(float);
        var tableName = VectorTableManager.GetCharacterTableName(projectID);

        await vectorTableManager.EnsureTableAsync(tableName, dimension, cancellationToken);

        await using var connection = await connectionFactory.CreateAsync(true, cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync
            (
                $"DELETE FROM \"{tableName}\" WHERE entry_id = @characterID",
                new { characterID },
                transaction
            );

            await connection.ExecuteAsync
            (
                $"INSERT INTO \"{tableName}\" (entry_id, embedding) VALUES (@characterID, @embedding)",
                new { characterID, embedding },
                transaction
            );

            await connection.ExecuteAsync
            (
                "UPDATE characters SET content_hash = @contentHash WHERE id = @characterID",
                new { characterID, contentHash },
                transaction
            );

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteEmbeddingAsync(long projectID, long characterID, CancellationToken cancellationToken = default)
    {
        var tableName = VectorTableManager.GetCharacterTableName(projectID);

        if (!await vectorTableManager.TableExistsAsync(tableName, cancellationToken))
            return;

        await using var connection = await connectionFactory.CreateAsync(true, cancellationToken);

        await connection.ExecuteAsync
        (
            $"DELETE FROM \"{tableName}\" WHERE entry_id = @characterID",
            new { characterID }
        );
    }

    public async Task<IReadOnlyList<(long characterID, float distance)>> SearchByVectorAsync
    (
        long                 projectID,
        byte[]               queryVector,
        int                  topK,
        IReadOnlyList<long>? candidateIDs      = null,
        CancellationToken    cancellationToken = default
    )
    {
        var tableName = VectorTableManager.GetCharacterTableName(projectID);

        if (!await vectorTableManager.TableExistsAsync(tableName, cancellationToken))
            return [];

        await using var connection = await connectionFactory.CreateAsync(true, cancellationToken);

        var sql = candidateIDs is { Count: > 0 } ?
                      $"""
                       SELECT entry_id AS CharacterID, distance AS Distance
                       FROM "{tableName}"
                       WHERE embedding MATCH @queryVector
                         AND k = @topK
                         AND entry_id IN @candidateIDs
                       ORDER BY distance
                       """ :
                      $"""
                       SELECT entry_id AS CharacterID, distance AS Distance
                       FROM "{tableName}"
                       WHERE embedding MATCH @queryVector
                         AND k = @topK
                       ORDER BY distance
                       """;

        var rows = await connection.QueryAsync<(long CharacterID, float Distance)>
                   (
                       sql,
                       new { queryVector, topK, candidateIDs }
                   );

        return rows.Select(r => (r.CharacterID, r.Distance)).ToList();
    }

    public async Task<IReadOnlyList<CharacterCategory>> GetCategoriesAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<CharacterCategory>
                   (
                       "SELECT * FROM character_categories WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.ToList();
    }

    public async Task<CharacterCategory> CreateCategoryAsync(CharacterCategory category, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

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
                         parentCategoryIDs = category.ParentCategoryIDs
                     }
                 );

        return category with { ID = id };
    }

    public async Task UpdateCategoryAsync(CharacterCategory category, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

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
                parentCategoryIDs = category.ParentCategoryIDs
            }
        );
    }

    public async Task DeleteCategoryAsync(long categoryID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        await connection.ExecuteAsync
        (
            "DELETE FROM character_categories WHERE id = @categoryID",
            new { categoryID }
        );
    }

    public async Task<IReadOnlyList<CharacterRelation>> GetRelationsAsync(long sessionID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<CharacterRelation>
                   (
                       "SELECT * FROM character_relations WHERE session_id = @sessionID ORDER BY id",
                       new { sessionID }
                   );

        return rows.ToList();
    }

    public async Task<IReadOnlyList<CharacterRelation>> GetRelationsByCharacterAsync(long characterID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<CharacterRelation>
                   (
                       "SELECT * FROM character_relations WHERE source_character_id = @characterID OR target_character_id = @characterID ORDER BY id",
                       new { characterID }
                   );

        return rows.ToList();
    }

    public async Task<IReadOnlyList<CharacterRelation>> GetRelationsByCharactersAsync
    (
        IReadOnlyList<long> characterIDs,
        CancellationToken   cancellationToken = default
    )
    {
        if (characterIDs.Count == 0)
            return [];

        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<CharacterRelation>
                   (
                       """
                       SELECT * FROM character_relations
                       WHERE source_character_id IN @characterIDs OR target_character_id IN @characterIDs
                       ORDER BY id
                       """,
                       new { characterIDs }
                   );

        return rows.ToList();
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
        long                 roundID,
        CancellationToken    cancellationToken = default
    )
    {
        await using var connection  = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;

        var existing = await connection.QueryFirstOrDefaultAsync<CharacterRelation>
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
            projectID = existing.ProjectID;
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
                    oldType        = existing.RelationType,
                    newType        = relationType,
                    oldDescription = existing.Description,
                    newDescription = description,
                    source,
                    reason,
                    sceneID,
                    createdAt = now
                },
                transaction
            );

            var oldRow = await RowReader.ReadRowAsync
                         (
                             connection,
                             "SELECT * FROM character_relations WHERE id = @relationID",
                             new { relationID },
                             transaction,
                             cancellationToken
                         );

            if (oldRow is not null)
            {
                await RoundChangeRepository.RecordAsync
                (
                    connection,
                    transaction,
                    sessionID,
                    roundID,
                    "character_relations",
                    relationID,
                    "update",
                    JsonSerializer.Serialize(oldRow, JsonOptions.Compact),
                    cancellationToken
                );
            }
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
                    source,
                    reason,
                    sceneID,
                    createdAt = now
                },
                transaction
            );

            await RoundChangeRepository.RecordAsync
            (
                connection,
                transaction,
                sessionID,
                roundID,
                "character_relations",
                relationID,
                "create",
                null,
                cancellationToken
            );
        }

        await transaction.CommitAsync(cancellationToken);

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
            CreatedAt         = now,
            UpdatedAt         = now
        };
    }

    public async Task<IReadOnlyList<CharacterRelationLog>> GetRelationLogsAsync
    (
        long              relationID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<CharacterRelationLog>
                   (
                       "SELECT * FROM character_relation_logs WHERE relation_id = @relationID ORDER BY id",
                       new { relationID }
                   );

        return rows.ToList();
    }

    public async Task<IReadOnlyList<CharacterScenePresence>> GetPresenceAsync(long sceneID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<CharacterScenePresence>
                   (
                       "SELECT character_id AS CharacterID, scene_id AS SceneID FROM character_scene_presence WHERE scene_id = @sceneID",
                       new { sceneID }
                   );

        return rows.ToList();
    }

    public Task EnterSceneAsync(long characterID, long sceneID, long sessionID, long roundID, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var affected = await connection.ExecuteAsync
                               (
                                   new CommandDefinition
                                   (
                                       """
                                       INSERT OR IGNORE INTO character_scene_presence (character_id, scene_id)
                                       VALUES (@characterID, @sceneID)
                                       """,
                                       new { characterID, sceneID },
                                       transaction,
                                       cancellationToken: token
                                   )
                               );

                if (affected > 0)
                {
                    await RoundChangeRepository.RecordAsync
                    (
                        connection,
                        transaction,
                        sessionID,
                        roundID,
                        "character_scene_presence",
                        0,
                        "create",
                        JsonSerializer.Serialize(new { character_id = characterID, scene_id = sceneID }, JsonOptions.Compact),
                        token
                    );
                }

                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public Task LeaveSceneAsync(long characterID, long sceneID, long sessionID, long roundID, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var affected = await connection.ExecuteAsync
                               (
                                   new CommandDefinition
                                   (
                                       "DELETE FROM character_scene_presence WHERE character_id = @characterID AND scene_id = @sceneID",
                                       new { characterID, sceneID },
                                       transaction,
                                       cancellationToken: token
                                   )
                               );

                if (affected > 0)
                {
                    await RoundChangeRepository.RecordAsync
                    (
                        connection,
                        transaction,
                        sessionID,
                        roundID,
                        "character_scene_presence",
                        0,
                        "delete",
                        JsonSerializer.Serialize(new { character_id = characterID, scene_id = sceneID }, JsonOptions.Compact),
                        token
                    );
                }

                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public async Task<CharacterCategoryResolution?> GetResolvedCategoriesAsync(long characterID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<CharacterCategoryResolution>
               (
                   "SELECT character_id, category_ids, attribute_ids FROM character_category_resolutions WHERE character_id = @characterID",
                   new { characterID }
               );
    }

    public async Task UpdateResolvedCategoriesAsync(CharacterCategoryResolution resolved, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

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
                categoryIDs  = resolved.CategoryIDs,
                attributeIDs = resolved.AttributeIDs
            }
        );
    }

    public async Task<IReadOnlyList<CharacterStateValue>> GetCharacterStateValuesBatchAsync
    (
        IReadOnlyList<long> characterIDs,
        CancellationToken   cancellationToken = default
    )
    {
        if (characterIDs.Count == 0)
            return [];

        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<CharacterStateValue>
                   (
                       "SELECT * FROM character_state_values WHERE character_id IN @characterIDs",
                       new { characterIDs }
                   );

        return rows.ToList();
    }

    public async Task<IReadOnlyList<CharacterStateValue>> GetCharacterStateValuesAsync(long characterID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<CharacterStateValue>
                   (
                       "SELECT * FROM character_state_values WHERE character_id = @characterID",
                       new { characterID }
                   );

        return rows.ToList();
    }

    public Task SetCharacterStateValueAsync
    (
        long              characterID,
        long              attributeID,
        string            value,
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var oldRow = await RowReader.ReadRowAsync
                             (
                                 connection,
                                 "SELECT * FROM character_state_values WHERE character_id = @characterID AND attribute_id = @attributeID",
                                 new { characterID, attributeID },
                                 transaction,
                                 token
                             );
                var now = DateTime.UtcNow;
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        """
                        INSERT INTO character_state_values (character_id, attribute_id, value, updated_at)
                        VALUES (@characterID, @attributeID, @value, @updatedAt)
                        ON CONFLICT(character_id, attribute_id)
                        DO UPDATE SET value = @value, updated_at = @updatedAt
                        """,
                        new { characterID, attributeID, value, updatedAt = now },
                        transaction,
                        cancellationToken: token
                    )
                );
                await RoundChangeRepository.RecordAsync
                (
                    connection,
                    transaction,
                    sessionID,
                    roundID,
                    "character_state_values",
                    0,
                    oldRow is null ?
                        "create" :
                        "update",
                    oldRow is null ?
                        JsonSerializer.Serialize(new { character_id = characterID, attribute_id = attributeID, value, updated_at = now }, JsonOptions.Compact) :
                        JsonSerializer.Serialize(oldRow,                                                                                  JsonOptions.Compact),
                    token
                );
                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    private static async Task ArchiveAsync
    (
        SqliteConnection  connection,
        DbTransaction     transaction,
        long              characterID,
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken
    )
    {
        var oldRow = await RowReader.ReadRowAsync
                     (
                         connection,
                         "SELECT * FROM characters WHERE id = @characterID",
                         new { characterID },
                         transaction,
                         cancellationToken
                     );

        if (oldRow is null)
            return;

        await connection.ExecuteAsync
        (
            new CommandDefinition
            (
                "UPDATE characters SET status = 'Archived', updated_at = @updatedAt WHERE id = @characterID",
                new { characterID, updatedAt = DateTime.UtcNow },
                transaction,
                cancellationToken: cancellationToken
            )
        );
        await RoundChangeRepository.RecordAsync
        (
            connection,
            transaction,
            sessionID,
            roundID,
            "characters",
            characterID,
            "update",
            JsonSerializer.Serialize(oldRow, JsonOptions.Compact),
            cancellationToken
        );
        var presenceRows = (await connection.QueryAsync
                            (
                                new CommandDefinition
                                (
                                    "SELECT character_id, scene_id FROM character_scene_presence WHERE character_id = @characterID",
                                    new { characterID },
                                    transaction,
                                    cancellationToken: cancellationToken
                                )
                            )).ToList();

        if (presenceRows.Count == 0)
            return;

        await connection.ExecuteAsync
        (
            new CommandDefinition
            (
                "DELETE FROM character_scene_presence WHERE character_id = @characterID",
                new { characterID },
                transaction,
                cancellationToken: cancellationToken
            )
        );

        foreach (var row in presenceRows)
        {
            await RoundChangeRepository.RecordAsync
            (
                connection,
                transaction,
                sessionID,
                roundID,
                "character_scene_presence",
                0,
                "delete",
                JsonSerializer.Serialize
                (
                    new { character_id = (long)row.character_id, scene_id = (long)row.scene_id },
                    JsonOptions.Compact
                ),
                cancellationToken
            );
        }
    }
}
