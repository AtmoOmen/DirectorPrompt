using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class MemoryRepository
(
    SqliteDatabaseScheduler scheduler
) : IMemoryRepository
{
    public Task<MemoryEntry?> GetByIDAsync(long id, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                var row = await connection.QueryFirstOrDefaultAsync<MemoryEntryRow>
                          (
                              new CommandDefinition
                              (
                                  "SELECT * FROM memory_entries WHERE id = @id",
                                  new { id },
                                  cancellationToken: token
                              )
                          );

                return row?.ToMemoryEntry();
            },
            cancellationToken: cancellationToken
        );

    public Task<IReadOnlyList<MemoryEntry>> GetPendingIndexEntriesAsync
    (
        long              projectID,
        string            embeddingFingerprint,
        int               limit,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync<IReadOnlyList<MemoryEntry>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<MemoryEntryRow>
                           (
                               new CommandDefinition
                               (
                                   """
                                   SELECT *
                                   FROM memory_entries
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
                                   new
                                   {
                                       projectID,
                                       embeddingFingerprint,
                                       limit = Math.Clamp(limit, 1, 128)
                                   },
                                   cancellationToken: token
                               )
                           );

                return rows.Select(row => row.ToMemoryEntry()).ToList();
            },
            SqliteWorkPriority.Maintenance,
            cancellationToken
        );

    public Task<IReadOnlyList<MemoryEntry>> GetByIdsAsync
    (
        long                sessionID,
        IReadOnlyList<long> memoryIDs,
        CancellationToken   cancellationToken = default
    )
    {
        if (memoryIDs.Count == 0)
            return Task.FromResult<IReadOnlyList<MemoryEntry>>([]);

        return scheduler.ExecuteAsync<IReadOnlyList<MemoryEntry>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<MemoryEntryRow>
                           (
                               new CommandDefinition
                               (
                                   "SELECT * FROM memory_entries WHERE session_id = @sessionID AND id IN @memoryIDs",
                                   new { sessionID, memoryIDs },
                                   cancellationToken: token
                               )
                           );

                return rows.Select(row => row.ToMemoryEntry()).ToList();
            },
            cancellationToken: cancellationToken
        );
    }

    public Task<IReadOnlyList<MemoryEntry>> GetRecentByCharacterAsync
    (
        long              characterID,
        long              maxTimelinePos,
        int               limit,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync<IReadOnlyList<MemoryEntry>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<MemoryEntryRow>
                           (
                               new CommandDefinition
                               (
                                   """
                                   SELECT *
                                   FROM memory_entries
                                   WHERE timeline_pos <= @maxTimelinePos
                                     AND EXISTS
                                     (
                                         SELECT 1
                                         FROM json_each(related_character_ids)
                                         WHERE value = @characterID
                                     )
                                   ORDER BY timeline_pos DESC, id DESC
                                   LIMIT @limit
                                   """,
                                   new
                                   {
                                       characterID,
                                       maxTimelinePos,
                                       limit = Math.Clamp(limit, 1, 200)
                                   },
                                   cancellationToken: token
                               )
                           );

                return rows.Select(row => row.ToMemoryEntry()).ToList();
            },
            cancellationToken: cancellationToken
        );

    public Task<MemoryPage> GetPageAsync
    (
        MemoryPageQuery   query,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                var pageSize   = Math.Clamp(query.PageSize, 1, 200);
                var searchText = query.SearchText?.Trim();
                var tag = string.IsNullOrWhiteSpace(query.Tag) ?
                              null :
                              query.Tag.Trim();

                var rows = await connection.QueryAsync<MemoryEntryRow>
                           (
                               new CommandDefinition
                               (
                                   """
                                   SELECT m.*
                                   FROM memory_entries m
                                   WHERE m.session_id = @SessionID
                                     AND m.timeline_pos <= @MaxTimelinePosition
                                     AND
                                     (
                                         @BeforeTimelinePosition IS NULL
                                         OR m.timeline_pos < @BeforeTimelinePosition
                                         OR (m.timeline_pos = @BeforeTimelinePosition AND m.id < @BeforeID)
                                     )
                                     AND (@SceneID IS NULL OR m.scene_id = @SceneID)
                                     AND (@TagJSON IS NULL OR instr(m.tags, @TagJSON) > 0)
                                     AND (@SearchText IS NULL OR instr(m.content, @SearchText) > 0)
                                   ORDER BY m.timeline_pos DESC, m.id DESC
                                   LIMIT @Take
                                   """,
                                   new
                                   {
                                       query.SessionID,
                                       query.MaxTimelinePosition,
                                       query.BeforeTimelinePosition,
                                       query.BeforeID,
                                       query.SceneID,
                                       TagJSON = tag is null ?
                                                     null :
                                                     JsonSerializer.Serialize(tag),
                                       SearchText = string.IsNullOrWhiteSpace(searchText) ?
                                                        null :
                                                        searchText,
                                       Take = pageSize + 1
                                   },
                                   cancellationToken: token
                               )
                           );
                var items   = rows.Select(row => row.ToMemoryEntry()).ToList();
                var hasMore = items.Count > pageSize;

                if (hasMore)
                    items.RemoveAt(items.Count - 1);

                var last = items.LastOrDefault();
                return new MemoryPage
                (
                    items,
                    hasMore ?
                        last?.TimelinePos :
                        null,
                    hasMore ?
                        last?.ID :
                        null
                );
            },
            cancellationToken: cancellationToken
        );

    public Task<MemoryEntry> CreateAsync
    (
        MemoryEntry       entry,
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken = default
    ) =>
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
                                     createdAt           = now.ToString("O"),
                                     updatedAt           = now.ToString("O")
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
                    "memory_entries",
                    id,
                    "create",
                    null,
                    token
                );
                await transaction.CommitAsync(token);

                return entry with { ID = id, CreatedAt = now, UpdatedAt = now };
            },
            cancellationToken: cancellationToken
        );

    public Task UpdateAsync(MemoryEntry entry, long sessionID, long roundID, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var oldRow = await RowReader.ReadRowAsync
                             (
                                 connection,
                                 "SELECT * FROM memory_entries WHERE id = @id",
                                 new { id = entry.ID },
                                 transaction,
                                 token
                             );
                var tags = JsonHelper.Serialize(entry.Tags);
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        """
                        UPDATE memory_entries
                        SET content = @content,
                            tags = @tags,
                            related_character_ids = @relatedCharacterIDs,
                            content_hash = CASE
                                WHEN content <> @content OR tags <> @tags THEN NULL
                                ELSE content_hash
                            END,
                            embedding_fingerprint = CASE
                                WHEN content <> @content OR tags <> @tags THEN NULL
                                ELSE embedding_fingerprint
                            END,
                            updated_at = @updatedAt
                        WHERE id = @id
                        """,
                        new
                        {
                            id      = entry.ID,
                            content = entry.Content,
                            tags,
                            relatedCharacterIDs = JsonHelper.Serialize(entry.RelatedCharacterIDs),
                            updatedAt           = DateTime.UtcNow.ToString("O")
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
                        "memory_entries",
                        entry.ID,
                        "update",
                        JsonSerializer.Serialize(oldRow),
                        token
                    );
                }

                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public Task<MemoryEntry> MergeAsync
    (
        IReadOnlyList<long> memoryIDs,
        long                sceneID,
        string              content,
        string[]            tags,
        long                sessionID,
        long                roundID,
        CancellationToken   cancellationToken = default
    )
    {
        var distinctIDs = memoryIDs.Distinct().ToArray();

        if (distinctIDs.Length == 0)
            throw new ArgumentException("合并记忆不能为空", nameof(memoryIDs));

        return scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var metadata = await connection.QuerySingleAsync<MergeSourceMetadata>
                               (
                                   new CommandDefinition
                                   (
                                       """
                                       SELECT COUNT(*) AS FoundCount,
                                              MIN(project_id) AS ProjectID,
                                              MAX(project_id) AS MaxProjectID,
                                              MIN(session_id) AS SessionID,
                                              MAX(session_id) AS MaxSessionID,
                                              MAX(timeline_pos) AS TimelinePosition
                                       FROM memory_entries
                                       WHERE id IN @distinctIDs
                                       """,
                                       new { distinctIDs },
                                       transaction,
                                       cancellationToken: token
                                   )
                               );

                if (metadata.FoundCount != distinctIDs.Length   ||
                    metadata.ProjectID is null                  ||
                    metadata.ProjectID != metadata.MaxProjectID ||
                    metadata.SessionID is null                  ||
                    metadata.SessionID != metadata.MaxSessionID)
                    throw new InvalidOperationException("待合并记忆不存在或不属于同一对话");

                var relatedIDs = await connection.QueryAsync<long>
                                 (
                                     new CommandDefinition
                                     (
                                         """
                                         SELECT DISTINCT CAST(j.value AS INTEGER)
                                         FROM memory_entries m
                                         JOIN json_each(m.related_character_ids) j
                                         WHERE m.id IN @distinctIDs
                                         """,
                                         new { distinctIDs },
                                         transaction,
                                         cancellationToken: token
                                     )
                                 );
                var sourceRows = new List<string>(distinctIDs.Length);

                foreach (var id in distinctIDs)
                {
                    var row = await RowReader.ReadRowAsync
                              (
                                  connection,
                                  "SELECT * FROM memory_entries WHERE id = @id",
                                  new { id },
                                  transaction,
                                  token
                              );
                    sourceRows.Add(JsonSerializer.Serialize(row));
                }

                var now                 = DateTime.UtcNow;
                var relatedCharacterIDs = relatedIDs.ToArray();
                var newID = await connection.ExecuteScalarAsync<long>
                            (
                                new CommandDefinition
                                (
                                    """
                                    INSERT INTO memory_entries (project_id, session_id, scene_id, timeline_pos, content, tags, related_character_ids, created_at, updated_at)
                                    VALUES (@projectID, @sessionID, @sceneID, @timelinePosition, @content, @tags, @relatedCharacterIDs, @createdAt, @updatedAt);
                                    SELECT last_insert_rowid();
                                    """,
                                    new
                                    {
                                        projectID = metadata.ProjectID.Value,
                                        sessionID = metadata.SessionID.Value,
                                        sceneID,
                                        timelinePosition = metadata.TimelinePosition,
                                        content,
                                        tags                = JsonHelper.Serialize(tags),
                                        relatedCharacterIDs = JsonHelper.Serialize(relatedCharacterIDs),
                                        createdAt           = now.ToString("O"),
                                        updatedAt           = now.ToString("O")
                                    },
                                    transaction,
                                    cancellationToken: token
                                )
                            );
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        "DELETE FROM memory_entries WHERE id IN @distinctIDs",
                        new { distinctIDs },
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
                    "memory_entries",
                    newID,
                    "create",
                    null,
                    token
                );

                for (var index = 0; index < distinctIDs.Length; index++)
                    await RoundChangeRepository.RecordAsync
                    (
                        connection,
                        transaction,
                        sessionID,
                        roundID,
                        "memory_entries",
                        distinctIDs[index],
                        "delete",
                        sourceRows[index],
                        token
                    );

                await transaction.CommitAsync(token);

                return new MemoryEntry
                {
                    ID                  = newID,
                    ProjectID           = metadata.ProjectID.Value,
                    SessionID           = metadata.SessionID.Value,
                    SceneID             = sceneID,
                    TimelinePos         = metadata.TimelinePosition,
                    Content             = content,
                    Tags                = tags,
                    RelatedCharacterIDs = relatedCharacterIDs,
                    CreatedAt           = now,
                    UpdatedAt           = now
                };
            },
            cancellationToken: cancellationToken
        );
    }

    public Task DeleteAsync(long id, long sessionID, long roundID, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var oldRow = await RowReader.ReadRowAsync
                             (
                                 connection,
                                 "SELECT * FROM memory_entries WHERE id = @id",
                                 new { id },
                                 transaction,
                                 token
                             );

                if (oldRow is null)
                    return;

                var projectID = Convert.ToInt64(oldRow["project_id"]);
                var sessionID = Convert.ToInt64(oldRow["session_id"]);
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        "DELETE FROM memory_entries WHERE id = @id",
                        new { id },
                        transaction,
                        cancellationToken: token
                    )
                );
                var tableName = VectorTableManager.GetMemoryTableName(projectID);

                if (await VectorTableManager.TableExistsAsync(connection, tableName, token))
                {
                    await connection.ExecuteAsync
                    (
                        new CommandDefinition
                        (
                            $"DELETE FROM \"{tableName}\" WHERE entry_id = @id",
                            new { id },
                            transaction,
                            cancellationToken: token
                        )
                    );
                }

                await RoundChangeRepository.RecordAsync
                (
                    connection,
                    transaction,
                    sessionID,
                    roundID,
                    "memory_entries",
                    id,
                    "delete",
                    JsonSerializer.Serialize(oldRow),
                    token
                );
                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    public Task SaveEmbeddingsAsync
    (
        long                                             projectID,
        long                                             entryID,
        long                                             sessionID,
        long                                             timelinePosition,
        IReadOnlyList<(string source, byte[] embedding)> vectors,
        string                                           contentHash,
        string                                           embeddingFingerprint,
        CancellationToken                                cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                var tableName = VectorTableManager.GetMemoryTableName(projectID);

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
                            $"INSERT INTO \"{tableName}\" (entry_id, source, session_id, timeline_pos, searchable, embedding) VALUES (@entryID, @source, @sessionID, @timelinePosition, 1, @embedding)",
                            vectors.Select
                            (vector => new
                                {
                                    entryID,
                                    sessionID,
                                    timelinePosition,
                                    vector.source,
                                    vector.embedding
                                }
                            ),
                            transaction,
                            cancellationToken: token
                        )
                    );
                }

                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        "UPDATE memory_entries SET content_hash = @contentHash, embedding_fingerprint = @embeddingFingerprint WHERE id = @entryID",
                        new { entryID, contentHash, embeddingFingerprint },
                        transaction,
                        cancellationToken: token
                    )
                );
                await transaction.CommitAsync(token);
            },
            SqliteWorkPriority.Maintenance,
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
                var tableName = VectorTableManager.GetMemoryTableName(projectID);

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
            SqliteWorkPriority.Maintenance,
            cancellationToken
        );

    public Task<IReadOnlyList<VectorSearchResult>> SearchByVectorAsync
    (
        long              projectID,
        long              sessionID,
        long              maxTimelinePosition,
        byte[]            queryVector,
        int               topK,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync<IReadOnlyList<VectorSearchResult>>
        (
            async (connection, token) =>
            {
                var tableName = VectorTableManager.GetMemoryTableName(projectID);

                if (!await VectorTableManager.TableExistsAsync(connection, tableName, token))
                    return [];

                var rows = await connection.QueryAsync<(long EntryID, string Source, float Distance)>
                           (
                               new CommandDefinition
                               (
                                   $"""
                                    SELECT entry_id AS EntryID, source AS Source, distance AS Distance
                                    FROM "{tableName}"
                                    WHERE embedding MATCH @queryVector
                                      AND k = @topK
                                      AND session_id = @sessionID
                                      AND timeline_pos <= @maxTimelinePosition
                                      AND searchable = 1
                                    ORDER BY distance
                                    """,
                                   new { queryVector, topK, sessionID, maxTimelinePosition },
                                   cancellationToken: token
                               )
                           );

                return rows
                       .GroupBy(row => row.EntryID)
                       .Select(group => group.MinBy(row => row.Distance))
                       .Select(row => new VectorSearchResult(row.EntryID, row.Source, row.Distance))
                       .OrderBy(row => row.Distance)
                       .Take(topK)
                       .ToList();
            },
            cancellationToken: cancellationToken
        );

    private sealed class MergeSourceMetadata
    {
        public long  FoundCount       { get; set; }
        public long? ProjectID        { get; set; }
        public long? MaxProjectID     { get; set; }
        public long? SessionID        { get; set; }
        public long? MaxSessionID     { get; set; }
        public long  TimelinePosition { get; set; }
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
        public string? Embedding_Fingerprint { get; set; }
        public string  Created_At            { get; set; } = string.Empty;
        public string  Updated_At            { get; set; } = string.Empty;

        public MemoryEntry ToMemoryEntry() =>
            new()
            {
                ID                   = ID,
                ProjectID            = Project_ID,
                SessionID            = Session_ID ?? 0,
                SceneID              = Scene_ID,
                TimelinePos          = Timeline_Pos,
                Content              = Content,
                Tags                 = JsonHelper.DeserializeStringArray(Tags),
                RelatedCharacterIDs  = JsonHelper.DeserializeInt64Array(Related_Character_IDs),
                ContentHash          = Content_Hash,
                EmbeddingFingerprint = Embedding_Fingerprint,
                CreatedAt            = DateTime.Parse(Created_At),
                UpdatedAt            = DateTime.Parse(Updated_At)
            };
    }
}
