using System.Data.Common;
using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class CharacterRepository : ICharacterRepository
{
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly VectorTableManager      vectorTableManager;
    private readonly SqliteDatabaseScheduler scheduler;

    public CharacterRepository
    (
        SqliteConnectionFactory connectionFactory,
        VectorTableManager      vectorTableManager,
        SqliteDatabaseScheduler scheduler
    )
    {
        this.connectionFactory  = connectionFactory;
        this.vectorTableManager = vectorTableManager;
        this.scheduler          = scheduler;
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

    public async Task<IReadOnlyList<Character>> GetActiveBySessionAsync(long sessionID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<CharacterRow>
                   (
                       "SELECT * FROM characters WHERE session_id = @sessionID AND status = 'active' ORDER BY last_touched_round DESC, id",
                       new { sessionID }
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

    public async Task<IReadOnlyList<Character>> GetByIDsAsync
    (
        long                sessionID,
        IReadOnlyList<long> characterIDs,
        CancellationToken   cancellationToken = default
    )
    {
        if (characterIDs.Count == 0)
            return [];

        await using var connection = await connectionFactory.CreateAsync(cancellationToken);
        var rows = await connection.QueryAsync<CharacterRow>
                   (
                       new CommandDefinition
                       (
                           "SELECT * FROM characters WHERE session_id = @sessionID AND id IN @characterIDs",
                           new { sessionID, characterIDs },
                           cancellationToken: cancellationToken
                       )
                   );

        return rows.Select(row => row.ToCharacter()).ToList();
    }

    public async Task<CharacterPage> GetPageAsync(CharacterPageQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var searchPattern = string.IsNullOrWhiteSpace(query.SearchText) ?
                                null :
                                $"%{query.SearchText.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
        var rows = await connection.QueryAsync<CharacterRow>
                   (
                       new CommandDefinition
                       (
                           """
                           SELECT c.*
                           FROM characters c
                           WHERE c.session_id = @SessionID
                             AND c.status = 'active'
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

        var items   = rows.Select(row => row.ToCharacter()).ToList();
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
                                     aliases          = JsonHelper.Serialize(character.Aliases),
                                     categoryIDs      = JsonHelper.Serialize(character.CategoryIDs),
                                     status           = character.Status.ToString().ToLowerInvariant(),
                                     touchCount       = character.TouchCount,
                                     lastTouchedRound = character.LastTouchedRound,
                                     createdAt        = now.ToString("O"),
                                     updatedAt        = now.ToString("O")
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
                            aliases     = JsonHelper.Serialize(character.Aliases),
                            categoryIDs = JsonHelper.Serialize(character.CategoryIDs),
                            status      = character.Status.ToString().ToLowerInvariant(),
                            updatedAt   = DateTime.UtcNow.ToString("O")
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
                        JsonSerializer.Serialize(oldRow),
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
                            status = 'active',
                            updated_at = @updatedAt
                        WHERE id = @characterID
                        """,
                        new { characterID, roundID, updatedAt = DateTime.UtcNow.ToString("O") },
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
                        JsonSerializer.Serialize(oldRow),
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
                                         AND status = 'active'
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

                var aliases = JsonHelper.DeserializeStringArray((string)oldRow["aliases"]!);

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
                            aliases   = JsonHelper.Serialize(aliases.Append(alias).ToArray()),
                            updatedAt = DateTime.UtcNow.ToString("O")
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
                    JsonSerializer.Serialize(oldRow),
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

        await using var connection = await connectionFactory.CreateAsync(cancellationToken, true);

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

        await using var connection = await connectionFactory.CreateAsync(cancellationToken, true);

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

        await using var connection = await connectionFactory.CreateAsync(cancellationToken, true);

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

    public async Task<IReadOnlyList<CharacterRelation>> GetRelationsByCharactersAsync
    (
        IReadOnlyList<long> characterIDs,
        CancellationToken   cancellationToken = default
    )
    {
        if (characterIDs.Count == 0)
            return [];

        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<CharacterRelationRow>
                   (
                       """
                       SELECT * FROM character_relations
                       WHERE source_character_id IN @characterIDs OR target_character_id IN @characterIDs
                       ORDER BY id
                       """,
                       new { characterIDs }
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
        long                 roundID,
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

        if (existing is not null)
        {
            var oldData = JsonSerializer.Serialize(existing);
            await RoundChangeRepository.RecordAsync
            (
                connection,
                transaction,
                sessionID,
                roundID,
                "character_relations",
                relationID,
                "update",
                oldData,
                cancellationToken
            );
        }
        else
        {
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
                        JsonSerializer.Serialize(new { character_id = characterID, scene_id = sceneID }),
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
                        JsonSerializer.Serialize(new { character_id = characterID, scene_id = sceneID }),
                        token
                    );
                }

                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

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

    public async Task<IReadOnlyList<CharacterStateValue>> GetCharacterStateValuesBatchAsync
    (
        IReadOnlyList<long> characterIDs,
        CancellationToken   cancellationToken = default
    )
    {
        if (characterIDs.Count == 0)
            return [];

        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<CharacterStateValueRow>
                   (
                       "SELECT * FROM character_state_values WHERE character_id IN @characterIDs",
                       new { characterIDs }
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
                var updatedAt = DateTime.UtcNow.ToString("O");
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
                        new { characterID, attributeID, value, updatedAt },
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
                        JsonSerializer.Serialize(new { character_id = characterID, attribute_id = attributeID, value, updated_at = updatedAt }) :
                        JsonSerializer.Serialize(oldRow),
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
                "UPDATE characters SET status = 'archived', updated_at = @updatedAt WHERE id = @characterID",
                new { characterID, updatedAt = DateTime.UtcNow.ToString("O") },
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
            JsonSerializer.Serialize(oldRow),
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
                    new { character_id = (long)row.character_id, scene_id = (long)row.scene_id }
                ),
                cancellationToken
            );
        }
    }

    private sealed class CharacterRow
    {
        public long    ID                 { get; set; }
        public long    Project_ID         { get; set; }
        public long    Session_ID         { get; set; }
        public string  Name               { get; set; } = string.Empty;
        public string  Description        { get; set; } = string.Empty;
        public string  Aliases            { get; set; } = "[]";
        public string  Category_IDs       { get; set; } = "[]";
        public string  Status             { get; set; } = "active";
        public int     Touch_Count        { get; set; }
        public long    Last_Touched_Round { get; set; }
        public string? Content_Hash       { get; set; }
        public string  Created_At         { get; set; } = string.Empty;
        public string  Updated_At         { get; set; } = string.Empty;

        public Character ToCharacter() =>
            new()
            {
                ID          = ID,
                ProjectID   = Project_ID,
                SessionID   = Session_ID,
                Name        = Name,
                Description = Description,
                Aliases     = JsonHelper.DeserializeStringArray(Aliases),
                CategoryIDs = JsonHelper.DeserializeInt64Array(Category_IDs),
                Status = Status switch
                {
                    "archived" => CharacterStatus.Archived,
                    _          => CharacterStatus.Active
                },
                TouchCount       = Touch_Count,
                LastTouchedRound = Last_Touched_Round,
                ContentHash      = Content_Hash,
                CreatedAt        = DateTime.Parse(Created_At),
                UpdatedAt        = DateTime.Parse(Updated_At)
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
        public long    Session_ID          { get; set; }
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
                SessionID         = Session_ID,
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
