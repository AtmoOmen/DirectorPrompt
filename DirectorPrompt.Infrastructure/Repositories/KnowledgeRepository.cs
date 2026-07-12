using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Serilog;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class KnowledgeRepository : IKnowledgeRepository
{
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly VectorTableManager      vectorTableManager;

    public KnowledgeRepository(SqliteConnectionFactory connectionFactory, VectorTableManager vectorTableManager)
    {
        this.connectionFactory  = connectionFactory;
        this.vectorTableManager = vectorTableManager;
    }

    public async Task<KnowledgeEntry?> GetByIDAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<KnowledgeEntryRow>
                  (
                      "SELECT * FROM knowledge_entries WHERE id = @id",
                      new { id }
                  );

        return row?.ToKnowledgeEntry();
    }

    public async Task<IReadOnlyList<KnowledgeEntry>> GetByProjectAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<KnowledgeEntryRow>
                   (
                       "SELECT * FROM knowledge_entries WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.Select(r => r.ToKnowledgeEntry()).ToList();
    }

    public async Task<IReadOnlyList<KnowledgeEntry>> GetActiveEntriesAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<KnowledgeEntryRow>
                   (
                       """
                       SELECT k.* FROM knowledge_entries k
                       WHERE k.project_id = @projectID
                         AND k.active = 1
                         AND k.group_id IS NOT NULL
                         AND (SELECT active FROM knowledge_groups WHERE id = k.group_id) = 1
                       ORDER BY k.id
                       """,
                       new { projectID }
                   );

        return rows.Select(r => r.ToKnowledgeEntry()).ToList();
    }

    public async Task<IReadOnlyList<KnowledgeEntry>> GetByGroupAsync(long groupID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<KnowledgeEntryRow>
                   (
                       "SELECT * FROM knowledge_entries WHERE group_id = @groupID ORDER BY id",
                       new { groupID }
                   );

        return rows.Select(r => r.ToKnowledgeEntry()).ToList();
    }

    public async Task<IReadOnlyList<KnowledgeEntry>> GetEntriesByIdsAsync
    (
        long                projectID,
        IReadOnlyList<long> entryIDs,
        CancellationToken   cancellationToken = default
    )
    {
        if (entryIDs.Count == 0)
            return [];

        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<KnowledgeEntryRow>
                   (
                       "SELECT * FROM knowledge_entries WHERE project_id = @projectID AND id IN @entryIDs ORDER BY id",
                       new { projectID, entryIDs }
                   );

        return rows.Select(r => r.ToKnowledgeEntry()).ToList();
    }

    public async Task<KnowledgeEntry> CreateAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var now = DateTime.UtcNow.ToString("O");

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
                         keywords  = JsonHelper.Serialize(entry.Keywords),
                         groupID   = entry.GroupID,
                         active = entry.Active ?
                                      1 :
                                      0,
                         createdAt = now,
                         updatedAt = now
                     }
                 );

        return entry with { ID = id };
    }

    public async Task UpdateAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            """
            UPDATE knowledge_entries
            SET remarks = @remarks,
                content = @content,
                keywords = @keywords,
                group_id = @groupID,
                active = @active,
                updated_at = @updatedAt
            WHERE id = @id
            """,
            new
            {
                id       = entry.ID,
                remarks  = entry.Remarks,
                content  = entry.Content,
                keywords = JsonHelper.Serialize(entry.Keywords),
                groupID  = entry.GroupID,
                active = entry.Active ?
                             1 :
                             0,
                updatedAt = DateTime.UtcNow.ToString("O")
            }
        );
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

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
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<KnowledgeGroupRow>
                   (
                       "SELECT * FROM knowledge_groups WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.Select(r => r.ToKnowledgeGroup()).ToList();
    }

    public async Task<KnowledgeGroup> CreateGroupAsync(KnowledgeGroup group, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

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
                         active = group.Active ?
                                      1 :
                                      0
                     }
                 );

        return group with { ID = id };
    }

    public async Task UpdateGroupAsync(KnowledgeGroup group, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

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
                active = group.Active ?
                             1 :
                             0
            }
        );
    }

    public async Task DeleteGroupAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

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

    public async Task<IReadOnlyList<KnowledgeEntityIndex>> GetEntityIndexAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync
                   (
                       """
                       SELECT i.entry_id AS EntryID, i.entity_name AS EntityName
                       FROM knowledge_entity_index i
                       JOIN knowledge_entries e ON e.id = i.entry_id
                       WHERE e.project_id = @projectID
                       """,
                       new { projectID }
                   );

        return rows.Select
        (r => new KnowledgeEntityIndex
            {
                EntryID    = r.EntryID,
                EntityName = r.EntityName
            }
        ).ToList();
    }

    public async Task AddEntityIndexAsync(long entryID, string entityName, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            """
            INSERT OR IGNORE INTO knowledge_entity_index (entry_id, entity_name)
            VALUES (@entryID, @entityName)
            """,
            new { entryID, entityName }
        );
    }

    public async Task RemoveEntityIndexAsync(long entryID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            "DELETE FROM knowledge_entity_index WHERE entry_id = @entryID",
            new { entryID }
        );
    }

    public async Task SaveEmbeddingsAsync
    (
        long                                           projectID,
        long                                           entryID,
        IReadOnlyList<(string source, byte[] embedding)> vectors,
        string                                         contentHash,
        CancellationToken                               cancellationToken = default
    )
    {
        if (vectors.Count == 0)
        {
            await DeleteEmbeddingAsync(projectID, entryID, cancellationToken);

            await using var conn = await connectionFactory.CreateAsync(cancellationToken);

            await conn.ExecuteAsync
            (
                "UPDATE knowledge_entries SET content_hash = @contentHash WHERE id = @entryID",
                new { entryID, contentHash }
            );

            return;
        }

        var dimension = vectors[0].embedding.Length / sizeof(float);
        var tableName = VectorTableManager.GetKnowledgeTableName(projectID);

        await vectorTableManager.EnsureMultiVectorTableAsync(tableName, dimension, cancellationToken);

        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync
            (
                $"DELETE FROM \"{tableName}\" WHERE entry_id = @entryID",
                new { entryID },
                transaction
            );

            await connection.ExecuteAsync
            (
                $"INSERT INTO \"{tableName}\" (entry_id, source, embedding) VALUES (@entryID, @source, @embedding)",
                vectors.Select(v => new { entryID, source = v.source, embedding = v.embedding }),
                transaction
            );

            await connection.ExecuteAsync
            (
                "UPDATE knowledge_entries SET content_hash = @contentHash WHERE id = @entryID",
                new { entryID, contentHash },
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

    public async Task DeleteEmbeddingAsync(long projectID, long entryID, CancellationToken cancellationToken = default)
    {
        var tableName = VectorTableManager.GetKnowledgeTableName(projectID);

        if (!await vectorTableManager.TableExistsAsync(tableName, cancellationToken))
            return;

        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            $"DELETE FROM \"{tableName}\" WHERE entry_id = @entryID",
            new { entryID }
        );
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchByVectorAsync
    (
        long                 projectID,
        byte[]               queryVector,
        int                  topK,
        IReadOnlyList<long>? candidateIDs      = null,
        CancellationToken    cancellationToken = default
    )
    {
        var tableName = VectorTableManager.GetKnowledgeTableName(projectID);

        if (!await vectorTableManager.TableExistsAsync(tableName, cancellationToken))
        {
            Log.Warning("知识向量表不存在: {Table}", tableName);
            return [];
        }

        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rowCount = await connection.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM \"{tableName}\"");
        Log.Information("知识向量表 {Table} 总行数: {Count}", tableName, rowCount);

        var vectorK = topK * 10;

        var sql = candidateIDs is { Count: > 0 } ?
                      $"""
                       SELECT entry_id AS EntryID, source AS Source, distance AS Distance
                       FROM "{tableName}"
                       WHERE embedding MATCH @queryVector
                         AND k = @vectorK
                         AND entry_id IN @candidateIDs
                       ORDER BY distance
                       """ :
                      $"""
                       SELECT entry_id AS EntryID, source AS Source, distance AS Distance
                       FROM "{tableName}"
                       WHERE embedding MATCH @queryVector
                         AND k = @vectorK
                       ORDER BY distance
                       """;

        var rows = (await connection.QueryAsync<(long EntryID, string Source, float Distance)>
                    (
                        sql,
                        new { queryVector, vectorK, candidateIDs }
                    )).ToList();

        var grouped = rows.GroupBy(r => r.EntryID).ToList();

        Log.Information("知识向量搜索: 原始行数={Raw}, 分组后={Grouped}", rows.Count, grouped.Count);

        return grouped
               .Select(g => g.OrderBy(r => r.Distance).First())
               .Select(r => new VectorSearchResult(r.EntryID, r.Source, r.Distance))
               .OrderBy(r => r.Distance)
               .Take(topK)
               .ToList();
    }

    private sealed class KnowledgeEntryRow
    {
        public long    ID           { get; set; }
        public long    Project_ID   { get; set; }
        public string  Remarks      { get; set; } = string.Empty;
        public string  Content      { get; set; } = string.Empty;
        public string  Keywords     { get; set; } = "[]";
        public long?   Group_ID     { get; set; }
        public int     Active       { get; set; }
        public string? Content_Hash { get; set; }
        public string  Created_At   { get; set; } = string.Empty;
        public string  Updated_At   { get; set; } = string.Empty;

        public KnowledgeEntry ToKnowledgeEntry() =>
            new()
            {
                ID          = ID,
                ProjectID   = Project_ID,
                Remarks     = Remarks,
                Content     = Content,
                Keywords    = JsonHelper.DeserializeStringArray(Keywords),
                GroupID     = Group_ID,
                Active      = Active != 0,
                ContentHash = Content_Hash,
                CreatedAt   = DateTime.Parse(Created_At),
                UpdatedAt   = DateTime.Parse(Updated_At)
            };
    }

    private sealed class KnowledgeGroupRow
    {
        public long    ID          { get; set; }
        public long    Project_ID  { get; set; }
        public string  Name        { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int     Active      { get; set; }

        public KnowledgeGroup ToKnowledgeGroup() =>
            new()
            {
                ID          = ID,
                ProjectID   = Project_ID,
                Name        = Name,
                Description = Description,
                Active      = Active != 0
            };
    }
}
