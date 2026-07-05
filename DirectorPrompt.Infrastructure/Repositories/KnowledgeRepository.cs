using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class KnowledgeRepository : IKnowledgeRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public KnowledgeRepository(SqliteConnectionFactory connectionFactory) =>
        this.connectionFactory = connectionFactory;

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
                         AND (k.group_id IS NULL OR
                              (SELECT active FROM knowledge_groups WHERE id = k.group_id) = 1)
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

    public async Task<KnowledgeEntry> CreateAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var now = DateTime.UtcNow.ToString("O");

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO knowledge_entries (project_id, title, content, tags, group_id, active, created_at, updated_at)
                     VALUES (@projectID, @title, @content, @tags, @groupID, @active, @createdAt, @updatedAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID = entry.ProjectID,
                         title     = entry.Title,
                         content   = entry.Content,
                         tags      = JsonHelper.Serialize(entry.Tags),
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
            SET title = @title,
                content = @content,
                tags = @tags,
                group_id = @groupID,
                active = @active,
                updated_at = @updatedAt
            WHERE id = @id
            """,
            new
            {
                id      = entry.ID,
                title   = entry.Title,
                content = entry.Content,
                tags    = JsonHelper.Serialize(entry.Tags),
                groupID = entry.GroupID,
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

    private sealed class KnowledgeEntryRow
    {
        public long   ID         { get; set; }
        public long   Project_ID { get; set; }
        public string Title      { get; set; } = string.Empty;
        public string Content    { get; set; } = string.Empty;
        public string Tags       { get; set; } = "[]";
        public long?  Group_ID   { get; set; }
        public int    Active     { get; set; }
        public string Created_At { get; set; } = string.Empty;
        public string Updated_At { get; set; } = string.Empty;

        public KnowledgeEntry ToKnowledgeEntry() =>
            new()
            {
                ID        = ID,
                ProjectID = Project_ID,
                Title     = Title,
                Content   = Content,
                Tags      = JsonHelper.DeserializeStringArray(Tags),
                GroupID   = Group_ID,
                Active    = Active != 0,
                CreatedAt = DateTime.Parse(Created_At),
                UpdatedAt = DateTime.Parse(Updated_At)
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
