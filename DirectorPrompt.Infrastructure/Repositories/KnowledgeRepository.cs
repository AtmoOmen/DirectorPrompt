using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Serilog;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class KnowledgeRepository
(
    SQLiteConnectionFactory connectionFactory,
    SQLiteDatabaseScheduler scheduler
)
    : IKnowledgeRepository
{
    public async Task<KnowledgeEntry?> GetByIDAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<KnowledgeEntry>
               (
                   "SELECT * FROM knowledge_entries WHERE id = @id",
                   new { id }
               );
    }

    public async Task<IReadOnlyList<KnowledgeEntry>> GetHeadersByProjectAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<KnowledgeEntry>
                   (
                       new CommandDefinition
                       (
                           """
                           SELECT id,
                                  project_id,
                                  remarks,
                                  '' AS content,
                                  '[]' AS keywords,
                                  group_id,
                                  active,
                                  content_hash,
                                  embedding_fingerprint,
                                  created_at,
                                  updated_at
                           FROM knowledge_entries
                           WHERE project_id = @projectID
                           ORDER BY id
                           """,
                           new { projectID },
                           cancellationToken: cancellationToken
                       )
                   );

        return rows.ToList();
    }

    public Task<IReadOnlyList<KnowledgeEntry>> GetPendingIndexEntriesAsync
    (
        long              projectID,
        string            embeddingFingerprint,
        int               limit,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync<IReadOnlyList<KnowledgeEntry>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<KnowledgeEntry>
                           (
                               new CommandDefinition
                               (
                                   """
                                   SELECT *
                                   FROM knowledge_entries
                                   WHERE project_id = @projectID
                                     AND
                                     (
                                         content_hash IS NULL
                                         OR embedding_fingerprint IS NULL
                                         OR embedding_fingerprint <> @embeddingFingerprint
                                     )
                                   ORDER BY id
                                   LIMIT @limit
                                   """,
                                   new { projectID, embeddingFingerprint, limit = Math.Clamp(limit, 1, 128) },
                                   cancellationToken: token
                               )
                           );

                return rows.ToList();
            },
            SQLiteWorkPriority.Maintenance,
            cancellationToken
        );

    public Task<IReadOnlyList<KnowledgeEntry>> GetByGroupAsync
    (
        long              groupID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync<IReadOnlyList<KnowledgeEntry>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<KnowledgeEntry>
                           (
                               new CommandDefinition
                               (
                                   "SELECT * FROM knowledge_entries WHERE group_id = @groupID ORDER BY id",
                                   new { groupID },
                                   cancellationToken: token
                               )
                           );

                return rows.ToList();
            },
            cancellationToken: cancellationToken
        );

    public Task<IReadOnlyList<KnowledgeEntry>> GetEntriesByIdsAsync
    (
        long                projectID,
        IReadOnlyList<long> entryIDs,
        CancellationToken   cancellationToken = default
    )
    {
        if (entryIDs.Count == 0)
            return Task.FromResult<IReadOnlyList<KnowledgeEntry>>([]);

        return scheduler.ExecuteAsync<IReadOnlyList<KnowledgeEntry>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<KnowledgeEntry>
                           (
                               new CommandDefinition
                               (
                                   "SELECT * FROM knowledge_entries WHERE project_id = @projectID AND id IN @entryIDs ORDER BY id",
                                   new { projectID, entryIDs },
                                   cancellationToken: token
                               )
                           );

                return rows.ToList();
            },
            cancellationToken: cancellationToken
        );
    }

    public Task<IReadOnlyList<KnowledgeEntry>> GetSearchableEntriesByIdsAsync
    (
        long                projectID,
        IReadOnlyList<long> entryIDs,
        IReadOnlyList<long> phaseEntryIDs,
        CancellationToken   cancellationToken = default
    )
    {
        if (entryIDs.Count == 0)
            return Task.FromResult<IReadOnlyList<KnowledgeEntry>>([]);

        return scheduler.ExecuteAsync<IReadOnlyList<KnowledgeEntry>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<KnowledgeEntry>
                           (
                               new CommandDefinition
                               (
                                   """
                                   SELECT k.*
                                   FROM knowledge_entries k
                                   LEFT JOIN knowledge_groups g ON g.id = k.group_id
                                   WHERE k.project_id = @projectID
                                     AND k.id IN @entryIDs
                                     AND
                                     (
                                         (k.active = 1 AND k.group_id IS NOT NULL AND g.active = 1)
                                         OR k.id IN @phaseEntryIDs
                                     )
                                   ORDER BY k.id
                                   """,
                                   new { projectID, entryIDs, phaseEntryIDs },
                                   cancellationToken: token
                               )
                           );

                return rows.ToList();
            },
            cancellationToken: cancellationToken
        );
    }

    public async Task<KnowledgeEntry> CreateAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var now = DateTime.UtcNow;

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO knowledge_entries (project_id, remarks, content, keywords, group_id, active, created_at, updated_at)
                     VALUES (@projectID, @remarks, @content, @keywords, @groupID, @active, @createdAt, @updatedAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID = entry.ProjectID,
                         remarks   = entry.Remarks,
                         content   = entry.Content,
                         keywords  = entry.Keywords,
                         groupID   = entry.GroupID,
                         active    = entry.Active,
                         createdAt = now,
                         updatedAt = now
                     }
                 );

        return entry with { ID = id };
    }

    public async Task UpdateAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        await connection.ExecuteAsync
        (
            """
            UPDATE knowledge_entries
            SET remarks = @remarks,
                content = @content,
                keywords = @keywords,
                group_id = @groupID,
                active = @active,
                content_hash = CASE
                    WHEN content <> @content OR keywords <> @keywords THEN NULL
                    ELSE content_hash
                END,
                embedding_fingerprint = CASE
                    WHEN content <> @content OR keywords <> @keywords THEN NULL
                    ELSE embedding_fingerprint
                END,
                updated_at = @updatedAt
            WHERE id = @id
            """,
            new
            {
                id        = entry.ID,
                remarks   = entry.Remarks,
                content   = entry.Content,
                keywords  = entry.Keywords,
                groupID   = entry.GroupID,
                active    = entry.Active,
                updatedAt = DateTime.UtcNow
            }
        );
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var projectID = await connection.QueryFirstOrDefaultAsync<long?>
                        (
                            "SELECT project_id FROM knowledge_entries WHERE id = @id",
                            new { id }
                        );

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync
            (
                "DELETE FROM knowledge_entity_index WHERE entry_id = @id",
                new { id },
                transaction
            );

            await connection.ExecuteAsync
            (
                "DELETE FROM knowledge_entries WHERE id = @id",
                new { id },
                transaction
            );

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        if (projectID is not null)
            await DeleteEmbeddingAsync(projectID.Value, id, cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeGroup>> GetGroupsAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<KnowledgeGroup>
                   (
                       "SELECT * FROM knowledge_groups WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.ToList();
    }

    public async Task<KnowledgeGroup> CreateGroupAsync(KnowledgeGroup group, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO knowledge_groups (project_id, name, description, active)
                     VALUES (@projectID, @name, @description, @active);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID   = group.ProjectID,
                         name        = group.Name,
                         description = group.Description,
                         active      = group.Active
                     }
                 );

        return group with { ID = id };
    }

    public async Task UpdateGroupAsync(KnowledgeGroup group, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        await connection.ExecuteAsync
        (
            """
            UPDATE knowledge_groups
            SET name = @name, description = @description, active = @active
            WHERE id = @id
            """,
            new
            {
                id          = group.ID,
                name        = group.Name,
                description = group.Description,
                active      = group.Active
            }
        );
    }

    public async Task DeleteGroupAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        await connection.ExecuteAsync
        (
            "UPDATE knowledge_entries SET group_id = NULL WHERE group_id = @id",
            new { id }
        );

        await connection.ExecuteAsync
        (
            "DELETE FROM knowledge_groups WHERE id = @id",
            new { id }
        );
    }

    public Task SaveEmbeddingsAsync
    (
        long                                             projectID,
        long                                             entryID,
        IReadOnlyList<(string source, byte[] embedding)> vectors,
        string                                           contentHash,
        string                                           embeddingFingerprint,
        CancellationToken                                cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                var tableName = VectorTableManager.GetKnowledgeTableName(projectID);

                if (vectors.Count > 0)
                {
                    var dimension = vectors[0].embedding.Length / sizeof(float);
                    await VectorTableManager.EnsureMultiVectorTableAsync(connection, tableName, dimension, token);
                }

                await using var transaction = await connection.BeginTransactionAsync(token);

                if (await VectorTableManager.TableExistsAsync(connection, tableName, token))
                {
                    await connection.ExecuteAsync
                    (
                        new CommandDefinition
                        (
                            $"DELETE FROM \"{tableName}\" WHERE entry_id = @entryID",
                            new { entryID },
                            transaction,
                            cancellationToken: token
                        )
                    );
                }

                if (vectors.Count > 0)
                {
                    await connection.ExecuteAsync
                    (
                        new CommandDefinition
                        (
                            $"INSERT INTO \"{tableName}\" (entry_id, source, session_id, timeline_pos, searchable, embedding) VALUES (@entryID, @source, 0, 0, 1, @embedding)",
                            vectors.Select(vector => new { entryID, vector.source, vector.embedding }),
                            transaction,
                            cancellationToken: token
                        )
                    );
                }

                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        "UPDATE knowledge_entries SET content_hash = @contentHash, embedding_fingerprint = @embeddingFingerprint WHERE id = @entryID",
                        new { entryID, contentHash, embeddingFingerprint },
                        transaction,
                        cancellationToken: token
                    )
                );
                await transaction.CommitAsync(token);
            },
            SQLiteWorkPriority.Maintenance,
            cancellationToken
        );

    public Task DeleteEmbeddingAsync
    (
        long              projectID,
        long              entryID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                var tableName = VectorTableManager.GetKnowledgeTableName(projectID);

                if (!await VectorTableManager.TableExistsAsync(connection, tableName, token))
                    return;

                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        $"DELETE FROM \"{tableName}\" WHERE entry_id = @entryID",
                        new { entryID },
                        cancellationToken: token
                    )
                );
            },
            SQLiteWorkPriority.Maintenance,
            cancellationToken
        );

    public Task<IReadOnlyList<VectorSearchResult>> SearchByVectorAsync
    (
        long              projectID,
        byte[]            queryVector,
        int               topK,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync<IReadOnlyList<VectorSearchResult>>
        (
            async (connection, token) =>
            {
                var tableName = VectorTableManager.GetKnowledgeTableName(projectID);

                if (!await VectorTableManager.TableExistsAsync(connection, tableName, token))
                {
                    Log.Warning("知识向量表不存在: {Table}", tableName);
                    return [];
                }

                var rows = (await connection.QueryAsync<(long EntryID, string Source, float Distance)>
                            (
                                new CommandDefinition
                                (
                                    $"""
                                     SELECT entry_id AS EntryID, source AS Source, distance AS Distance
                                     FROM "{tableName}"
                                     WHERE embedding MATCH @queryVector
                                       AND k = @topK
                                       AND searchable = 1
                                     ORDER BY distance
                                     """,
                                    new { queryVector, topK },
                                    cancellationToken: token
                                )
                            )).ToList();
                var grouped = rows.GroupBy(row => row.EntryID).ToList();

                Log.Information("知识向量搜索: 原始行数={Raw}, 分组后={Grouped}", rows.Count, grouped.Count);

                return grouped.Select(group => group.MinBy(row => row.Distance))
                              .Select(row => new VectorSearchResult(row.EntryID, row.Source, row.Distance))
                              .OrderBy(row => row.Distance)
                              .Take(topK)
                              .ToList();
            },
            cancellationToken: cancellationToken
        );
}
