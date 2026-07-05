using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class MemoryRepository : IMemoryRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public MemoryRepository(SqliteConnectionFactory connectionFactory) =>
        this.connectionFactory = connectionFactory;

    public async Task<MemoryEntry?> GetByIDAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<MemoryEntryRow>
                  (
                      "SELECT * FROM memory_entries WHERE id = @id",
                      new { id }
                  );

        return row?.ToMemoryEntry();
    }

    public async Task<IReadOnlyList<MemoryEntry>> GetByProjectAsync
    (
        long              projectID,
        long              maxTimelinePos,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<MemoryEntryRow>
                   (
                       "SELECT * FROM memory_entries WHERE project_id = @projectID AND timeline_pos <= @maxTimelinePos ORDER BY timeline_pos DESC",
                       new { projectID, maxTimelinePos }
                   );

        return rows.Select(r => r.ToMemoryEntry()).ToList();
    }

    public async Task<IReadOnlyList<MemoryEntry>> GetBySceneAsync(long sceneID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<MemoryEntryRow>
                   (
                       "SELECT * FROM memory_entries WHERE scene_id = @sceneID ORDER BY id",
                       new { sceneID }
                   );

        return rows.Select(r => r.ToMemoryEntry()).ToList();
    }

    public async Task<IReadOnlyList<MemoryEntry>> GetByCharacterAsync
    (
        long              characterID,
        long              maxTimelinePos,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<MemoryEntryRow>
                   (
                       """
                       SELECT * FROM memory_entries
                       WHERE timeline_pos <= @maxTimelinePos
                         AND EXISTS (
                           SELECT 1 FROM json_each(related_character_ids)
                           WHERE value = @characterID
                         )
                       ORDER BY timeline_pos DESC
                       """,
                       new { characterID, maxTimelinePos }
                   );

        return rows.Select(r => r.ToMemoryEntry()).ToList();
    }

    public async Task<MemoryEntry> CreateAsync(MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var now = DateTime.UtcNow.ToString("O");

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO memory_entries (project_id, scene_id, timeline_pos, content, tags, related_character_ids, created_at, updated_at)
                     VALUES (@projectID, @sceneID, @timelinePos, @content, @tags, @relatedCharacterIDs, @createdAt, @updatedAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID           = entry.ProjectID,
                         sceneID             = entry.SceneID,
                         timelinePos         = entry.TimelinePos,
                         content             = entry.Content,
                         tags                = JsonHelper.Serialize(entry.Tags),
                         relatedCharacterIDs = JsonHelper.Serialize(entry.RelatedCharacterIDs),
                         createdAt           = now,
                         updatedAt           = now
                     }
                 );

        return entry with { ID = id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
    }

    public async Task UpdateAsync(MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            """
            UPDATE memory_entries
            SET content = @content,
                tags = @tags,
                related_character_ids = @relatedCharacterIDs,
                updated_at = @updatedAt
            WHERE id = @id
            """,
            new
            {
                id                  = entry.ID,
                content             = entry.Content,
                tags                = JsonHelper.Serialize(entry.Tags),
                relatedCharacterIDs = JsonHelper.Serialize(entry.RelatedCharacterIDs),
                updatedAt           = DateTime.UtcNow.ToString("O")
            }
        );
    }

    public async Task<MemoryEntry> MergeAsync
    (
        IReadOnlyList<long> memoryIDs,
        long                sceneID,
        string              content,
        string[]            tags,
        CancellationToken   cancellationToken = default
    )
    {
        await using var connection  = await connectionFactory.CreateAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow.ToString("O");

        var relatedIDs = new HashSet<long>();

        foreach (var id in memoryIDs)
        {
            var json = await connection.QueryFirstOrDefaultAsync<string>
                       (
                           "SELECT related_character_ids FROM memory_entries WHERE id = @id",
                           new { id },
                           transaction
                       );

            foreach (var cid in JsonHelper.DeserializeInt64Array(json ?? "[]"))
                relatedIDs.Add(cid);
        }

        var projectID = await connection.QueryFirstAsync<long>
                        (
                            "SELECT project_id FROM memory_entries WHERE id = @firstID",
                            new { firstID = memoryIDs[0] },
                            transaction
                        );

        var timelinePos = await connection.QueryFirstAsync<long>
                          (
                              "SELECT MAX(timeline_pos) FROM memory_entries WHERE id IN @ids",
                              new { ids = memoryIDs },
                              transaction
                          );

        var newID = await connection.ExecuteScalarAsync<long>
                    (
                        """
                        INSERT INTO memory_entries (project_id, scene_id, timeline_pos, content, tags, related_character_ids, created_at, updated_at)
                        VALUES (@projectID, @sceneID, @timelinePos, @content, @tags, @relatedCharacterIDs, @createdAt, @updatedAt);
                        SELECT last_insert_rowid();
                        """,
                        new
                        {
                            projectID,
                            sceneID,
                            timelinePos,
                            content,
                            tags                = JsonHelper.Serialize(tags),
                            relatedCharacterIDs = JsonHelper.Serialize(relatedIDs.ToArray()),
                            createdAt           = now,
                            updatedAt           = now
                        },
                        transaction
                    );

        await connection.ExecuteAsync
        (
            "DELETE FROM memory_entries WHERE id IN @ids",
            new { ids = memoryIDs },
            transaction
        );

        await transaction.CommitAsync(cancellationToken);

        return new MemoryEntry
        {
            ID                  = newID,
            ProjectID           = projectID,
            SceneID             = sceneID,
            TimelinePos         = timelinePos,
            Content             = content,
            Tags                = tags,
            RelatedCharacterIDs = relatedIDs.ToArray(),
            CreatedAt           = DateTime.UtcNow,
            UpdatedAt           = DateTime.UtcNow
        };
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync("DELETE FROM memory_entries WHERE id = @id", new { id });
    }

    private sealed class MemoryEntryRow
    {
        public long   ID                    { get; set; }
        public long   Project_ID            { get; set; }
        public long   Scene_ID              { get; set; }
        public long   Timeline_Pos          { get; set; }
        public string Content               { get; set; } = string.Empty;
        public string Tags                  { get; set; } = "[]";
        public string Related_Character_IDs { get; set; } = "[]";
        public string Created_At            { get; set; } = string.Empty;
        public string Updated_At            { get; set; } = string.Empty;

        public MemoryEntry ToMemoryEntry() =>
            new()
            {
                ID                  = ID,
                ProjectID           = Project_ID,
                SceneID             = Scene_ID,
                TimelinePos         = Timeline_Pos,
                Content             = Content,
                Tags                = JsonHelper.DeserializeStringArray(Tags),
                RelatedCharacterIDs = JsonHelper.DeserializeInt64Array(Related_Character_IDs),
                CreatedAt           = DateTime.Parse(Created_At),
                UpdatedAt           = DateTime.Parse(Updated_At)
            };
    }
}
