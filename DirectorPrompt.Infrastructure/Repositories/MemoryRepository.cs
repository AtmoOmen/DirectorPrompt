using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Microsoft.Data.Sqlite;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class MemoryRepository : IMemoryRepository
{
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly IRoundChangeRepository  roundChangeRepository;
    private readonly VectorTableManager      vectorTableManager;

    public MemoryRepository(SqliteConnectionFactory connectionFactory, IRoundChangeRepository roundChangeRepository, VectorTableManager vectorTableManager)
    {
        this.connectionFactory     = connectionFactory;
        this.roundChangeRepository = roundChangeRepository;
        this.vectorTableManager    = vectorTableManager;
    }

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

    public async Task<IReadOnlyList<MemoryEntry>> GetBySessionAsync
    (
        long              sessionID,
        long              maxTimelinePos,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<MemoryEntryRow>
                   (
                       "SELECT * FROM memory_entries WHERE session_id = @sessionID AND timeline_pos <= @maxTimelinePos ORDER BY timeline_pos DESC",
                       new { sessionID, maxTimelinePos }
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
                     INSERT INTO memory_entries (project_id, session_id, scene_id, timeline_pos, content, tags, related_character_ids, created_at, updated_at)
                     VALUES (@projectID, @sessionID, @sceneID, @timelinePos, @content, @tags, @relatedCharacterIDs, @createdAt, @updatedAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID           = entry.ProjectID,
                         sessionID           = entry.SessionID,
                         sceneID             = entry.SceneID,
                         timelinePos         = entry.TimelinePos,
                         content             = entry.Content,
                         tags                = JsonHelper.Serialize(entry.Tags),
                         relatedCharacterIDs = JsonHelper.Serialize(entry.RelatedCharacterIDs),
                         createdAt           = now,
                         updatedAt           = now
                     }
                 );

        var result = entry with { ID = id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

        await roundChangeRepository.RecordCreateAsync(RoundContext.Current ?? 0, "memory_entries", id, null, cancellationToken);

        return result;
    }

    public async Task UpdateAsync(MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var oldRow = await RowReader.ReadRowAsync
                     (
                         connection,
                         "SELECT * FROM memory_entries WHERE id = @id",
                         new { id = entry.ID },
                         cancellationToken: cancellationToken
                     );

        var oldDataJSON = oldRow is null ?
                              "{}" :
                              JsonSerializer.Serialize(oldRow);

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

        await roundChangeRepository.RecordUpdateAsync(RoundContext.Current ?? 0, "memory_entries", entry.ID, oldDataJSON, cancellationToken);
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

        var sessionID = await connection.QueryFirstAsync<long>
                        (
                            "SELECT session_id FROM memory_entries WHERE id = @firstID",
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
                        INSERT INTO memory_entries (project_id, session_id, scene_id, timeline_pos, content, tags, related_character_ids, created_at, updated_at)
                        VALUES (@projectID, @sessionID, @sceneID, @timelinePos, @content, @tags, @relatedCharacterIDs, @createdAt, @updatedAt);
                        SELECT last_insert_rowid();
                        """,
                        new
                        {
                            projectID,
                            sessionID,
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

        var sourceRows = new List<string>();

        foreach (var id in memoryIDs)
        {
            var row = await RowReader.ReadRowAsync
                      (
                          connection,
                          "SELECT * FROM memory_entries WHERE id = @id",
                          new { id },
                          transaction,
                          cancellationToken
                      );
            sourceRows.Add
            (
                row is null ?
                    "{}" :
                    JsonSerializer.Serialize(row)
            );
        }

        await connection.ExecuteAsync
        (
            "DELETE FROM memory_entries WHERE id IN @ids",
            new { ids = memoryIDs },
            transaction
        );

        await transaction.CommitAsync(cancellationToken);

        var roundID = RoundContext.Current ?? 0;
        await roundChangeRepository.RecordCreateAsync(roundID, "memory_entries", newID, null, cancellationToken);
        for (var i = 0; i < memoryIDs.Count; i++)
            await roundChangeRepository.RecordDeleteAsync(roundID, "memory_entries", memoryIDs[i], sourceRows[i], cancellationToken);

        return new MemoryEntry
        {
            ID                  = newID,
            ProjectID           = projectID,
            SessionID           = sessionID,
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

        var (oldRow, projectID) = await GetRowAndProjectAsync(connection, id, cancellationToken);

        await connection.ExecuteAsync("DELETE FROM memory_entries WHERE id = @id", new { id });

        if (oldRow is not null)
            await roundChangeRepository.RecordDeleteAsync(RoundContext.Current ?? 0, "memory_entries", id, JsonSerializer.Serialize(oldRow), cancellationToken);

        if (projectID is not null)
            await DeleteEmbeddingAsync(projectID.Value, id, cancellationToken);
    }

    private static async Task<(Dictionary<string, object?>? row, long? projectID)> GetRowAndProjectAsync
    (
        SqliteConnection  connection,
        long              id,
        CancellationToken cancellationToken
    )
    {
        var oldRow = await RowReader.ReadRowAsync
                     (
                         connection,
                         "SELECT * FROM memory_entries WHERE id = @id",
                         new { id },
                         cancellationToken: cancellationToken
                     );

        long? projectID = null;

        if (oldRow is not null && oldRow.TryGetValue("project_id", out var pid))
            projectID = Convert.ToInt64(pid);

        return (oldRow, projectID);
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
            await using var conn = await connectionFactory.CreateAsync(cancellationToken);

            await conn.ExecuteAsync
            (
                "UPDATE memory_entries SET content_hash = @contentHash WHERE id = @entryID",
                new { entryID, contentHash }
            );

            return;
        }

        var dimension = vectors[0].embedding.Length / sizeof(float);
        var tableName = VectorTableManager.GetMemoryTableName(projectID);

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
                "UPDATE memory_entries SET content_hash = @contentHash WHERE id = @entryID",
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
        var tableName = VectorTableManager.GetMemoryTableName(projectID);

        if (!await vectorTableManager.TableExistsAsync(tableName, cancellationToken))
            return;

        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            $"DELETE FROM \"{tableName}\" WHERE entry_id = @entryID",
            new { entryID }
        );
    }

    public async Task<IReadOnlyList<(long entryID, float distance)>> SearchByVectorAsync
    (
        long                 projectID,
        byte[]               queryVector,
        int                  topK,
        IReadOnlyList<long>? candidateIDs      = null,
        CancellationToken    cancellationToken = default
    )
    {
        var tableName = VectorTableManager.GetMemoryTableName(projectID);

        if (!await vectorTableManager.TableExistsAsync(tableName, cancellationToken))
            return [];

        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var vectorK = topK * 10;

        var sql = candidateIDs is { Count: > 0 } ?
                      $"""
                       SELECT entry_id AS EntryID, distance AS Distance
                       FROM "{tableName}"
                       WHERE embedding MATCH @queryVector
                         AND k = @vectorK
                         AND entry_id IN @candidateIDs
                       ORDER BY distance
                       """ :
                      $"""
                       SELECT entry_id AS EntryID, distance AS Distance
                       FROM "{tableName}"
                       WHERE embedding MATCH @queryVector
                         AND k = @vectorK
                       ORDER BY distance
                       """;

        var rows = await connection.QueryAsync<(long EntryID, float Distance)>
                   (
                       sql,
                       new { queryVector, vectorK, candidateIDs }
                   );

        return rows
               .GroupBy(r => r.EntryID)
               .Select(g => (entryID: g.Key, distance: g.Min(r => r.Distance)))
               .OrderBy(r => r.distance)
               .Take(topK)
               .ToList();
    }

    private sealed class MemoryEntryRow
    {
        public long    ID                    { get; set; }
        public long    Project_ID            { get; set; }
        public long?   Session_ID            { get; set; }
        public long    Scene_ID              { get; set; }
        public long    Timeline_Pos          { get; set; }
        public string  Content               { get; set; } = string.Empty;
        public string  Tags                  { get; set; } = "[]";
        public string  Related_Character_IDs { get; set; } = "[]";
        public string? Content_Hash          { get; set; }
        public string  Created_At            { get; set; } = string.Empty;
        public string  Updated_At            { get; set; } = string.Empty;

        public MemoryEntry ToMemoryEntry() =>
            new()
            {
                ID                  = ID,
                ProjectID           = Project_ID,
                SessionID           = Session_ID ?? 0,
                SceneID             = Scene_ID,
                TimelinePos         = Timeline_Pos,
                Content             = Content,
                Tags                = JsonHelper.DeserializeStringArray(Tags),
                RelatedCharacterIDs = JsonHelper.DeserializeInt64Array(Related_Character_IDs),
                ContentHash         = Content_Hash,
                CreatedAt           = DateTime.Parse(Created_At),
                UpdatedAt           = DateTime.Parse(Updated_At)
            };
    }
}
