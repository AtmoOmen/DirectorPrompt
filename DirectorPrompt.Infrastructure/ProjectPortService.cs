using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Services;
using Microsoft.Data.Sqlite;

namespace DirectorPrompt.Infrastructure;

public sealed class ProjectPortService
(
    SQLiteConnectionFactory connectionFactory,
    ILocalizationService    localizationService
) : IProjectPortService
{
    private const string PACKAGE_FORMAT = "DirectorPrompt-Project-Package";

    private const int PACKAGE_VERSION = 1;

    public async Task ExportAsync(long projectID, string filePath, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var project = await QueryProjectAsync(connection, projectID, cancellationToken);

        if (project is null)
            throw new InvalidOperationException($"项目不存在: ID={projectID}");

        var categories  = await QueryCharacterCategoriesAsync(connection, projectID, cancellationToken);
        var attributes  = await QueryStateAttributesAsync(connection, projectID, cancellationToken);
        var groups      = await QueryKnowledgeGroupsAsync(connection, projectID, cancellationToken);
        var entries     = await QueryKnowledgeEntriesAsync(connection, projectID, cancellationToken);
        var entityIndex = await QueryKnowledgeEntityIndexAsync(connection, projectID, cancellationToken);

        var packageData = new ProjectPackageData
        {
            Project              = project,
            CharacterCategories  = categories,
            StateAttributes      = attributes,
            KnowledgeGroups      = groups,
            KnowledgeEntries     = entries.Select(e => e with { ContentHash = null }).ToList(),
            KnowledgeEntityIndex = entityIndex
        };

        var manifest = new PackageManifest
        {
            Format      = PACKAGE_FORMAT,
            Version     = PACKAGE_VERSION,
            ExportedAt  = DateTime.UtcNow,
            ProjectName = project.Name
        };

        using var zip = ZipFile.Open(filePath, ZipArchiveMode.Create);

        var manifestEntry = zip.CreateEntry("manifest.json");

        await using (var manifestStream = manifestEntry.Open())
            await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions.Default, cancellationToken);

        var dataEntry = zip.CreateEntry("project.json");

        await using (var dataStream = dataEntry.Open())
            await JsonSerializer.SerializeAsync(dataStream, packageData, JsonOptions.Default, cancellationToken);
    }

    public async Task<ProjectImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var zip = ZipFile.OpenRead(filePath);

        var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("无效的项目包: 缺少 manifest.json");

        PackageManifest manifest;

        using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<PackageManifest>(manifestStream, JsonOptions.Default, cancellationToken) ??
                       throw new InvalidDataException("无效的项目包: manifest.json 解析失败");
        }

        if (manifest.Format != PACKAGE_FORMAT)
            throw new InvalidDataException($"不支持的项目包格式: {manifest.Format}");

        if (manifest.Version > PACKAGE_VERSION)
            throw new InvalidDataException($"项目包版本过高: v{manifest.Version}, 当前支持: v{PACKAGE_VERSION}");

        var dataEntry = zip.GetEntry("project.json") ?? throw new InvalidDataException("无效的项目包: 缺少 project.json");

        ProjectPackageData data;

        using (var dataStream = dataEntry.Open())
        {
            data = await JsonSerializer.DeserializeAsync<ProjectPackageData>(dataStream, JsonOptions.Default, cancellationToken) ??
                   throw new InvalidDataException("无效的项目包: project.json 解析失败");
        }

        if (data.Project is null)
            throw new InvalidDataException("无效的项目包: 缺少项目数据");

        await using var connection  = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var now          = DateTime.UtcNow;
            var newProjectID = await InsertProjectAsync(connection, transaction, data.Project, now, cancellationToken);

            var categoryIDMap = await InsertCharacterCategoriesAsync
                                (
                                    connection,
                                    transaction,
                                    data.CharacterCategories ?? [],
                                    newProjectID,
                                    cancellationToken
                                );

            await UpdateCategoryParentIDsAsync
            (
                connection,
                transaction,
                data.CharacterCategories ?? [],
                categoryIDMap,
                cancellationToken
            );

            await InsertStateAttributesAsync
            (
                connection,
                transaction,
                data.StateAttributes ?? [],
                newProjectID,
                categoryIDMap,
                cancellationToken
            );

            var groupIDMap = await InsertKnowledgeGroupsAsync
                             (
                                 connection,
                                 transaction,
                                 data.KnowledgeGroups ?? [],
                                 newProjectID,
                                 cancellationToken
                             );

            var entryIDMap = await InsertKnowledgeEntriesAsync
                             (
                                 connection,
                                 transaction,
                                 data.KnowledgeEntries ?? [],
                                 newProjectID,
                                 groupIDMap,
                                 cancellationToken
                             );

            await InsertKnowledgeEntityIndexAsync
            (
                connection,
                transaction,
                data.KnowledgeEntityIndex ?? [],
                entryIDMap,
                cancellationToken
            );

            await transaction.CommitAsync(cancellationToken);

            return new ProjectImportResult
            {
                ProjectID           = newProjectID,
                ProjectName         = data.Project.Name,
                KnowledgeEntryCount = data.KnowledgeEntries?.Count ?? 0,
                StateAttributeCount = data.StateAttributes?.Count  ?? 0
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<Project?> QueryProjectAsync
    (
        SqliteConnection  connection,
        long              projectID,
        CancellationToken cancellationToken
    ) =>
        await connection.QueryFirstOrDefaultAsync<Project>
        (
            "SELECT * FROM projects WHERE id = @id",
            new { id = projectID }
        );

    private static async Task<List<CharacterCategory>> QueryCharacterCategoriesAsync
    (
        SqliteConnection  connection,
        long              projectID,
        CancellationToken cancellationToken
    )
    {
        var rows = await connection.QueryAsync<CharacterCategory>
                   (
                       "SELECT * FROM character_categories WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.ToList();
    }

    private static async Task<List<StateAttribute>> QueryStateAttributesAsync
    (
        SqliteConnection  connection,
        long              projectID,
        CancellationToken cancellationToken
    )
    {
        var rows = await connection.QueryAsync<StateAttribute>
                   (
                       "SELECT * FROM state_attributes WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.ToList();
    }

    private static async Task<List<KnowledgeGroup>> QueryKnowledgeGroupsAsync
    (
        SqliteConnection  connection,
        long              projectID,
        CancellationToken cancellationToken
    )
    {
        var rows = await connection.QueryAsync<KnowledgeGroup>
                   (
                       "SELECT * FROM knowledge_groups WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.ToList();
    }

    private static async Task<List<KnowledgeEntry>> QueryKnowledgeEntriesAsync
    (
        SqliteConnection  connection,
        long              projectID,
        CancellationToken cancellationToken
    )
    {
        var rows = await connection.QueryAsync<KnowledgeEntry>
                   (
                       "SELECT * FROM knowledge_entries WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.ToList();
    }

    private static async Task<List<KnowledgeEntityIndex>> QueryKnowledgeEntityIndexAsync
    (
        SqliteConnection  connection,
        long              projectID,
        CancellationToken cancellationToken
    )
    {
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
                           EntryID    = (long)r.EntryID,
                           EntityName = (string)r.EntityName
                       }
                   )
                   .ToList();
    }

    private static async Task<long> InsertProjectAsync
    (
        SqliteConnection  connection,
        SqliteTransaction transaction,
        Project           project,
        DateTime          now,
        CancellationToken cancellationToken
    )
    {
        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO projects (name, description, opening_message, created_at, updated_at)
                     VALUES (@name, @description, @openingMessage, @createdAt, @updatedAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         name           = project.Name,
                         description    = project.Description,
                         openingMessage = project.OpeningMessage,
                         createdAt      = now,
                         updatedAt      = now
                     },
                     transaction
                 );

        return id;
    }

    private static async Task<Dictionary<long, long>> InsertCharacterCategoriesAsync
    (
        SqliteConnection        connection,
        SqliteTransaction       transaction,
        List<CharacterCategory> categories,
        long                    newProjectID,
        CancellationToken       cancellationToken
    )
    {
        var idMap = new Dictionary<long, long>();

        foreach (var category in categories)
        {
            var id = await connection.ExecuteScalarAsync<long>
                     (
                         """
                         INSERT INTO character_categories (project_id, name, description, parent_category_ids)
                         VALUES (@projectID, @name, @description, @parentCategoryIDs);
                         SELECT last_insert_rowid();
                         """,
                         new
                         {
                             projectID         = newProjectID,
                             name              = category.Name,
                             description       = category.Description,
                             parentCategoryIDs = "[]"
                         },
                         transaction
                     );

            idMap[category.ID] = id;
        }

        return idMap;
    }

    private static async Task UpdateCategoryParentIDsAsync
    (
        SqliteConnection        connection,
        SqliteTransaction       transaction,
        List<CharacterCategory> categories,
        Dictionary<long, long>  categoryIDMap,
        CancellationToken       cancellationToken
    )
    {
        foreach (var category in categories)
        {
            if (category.ParentCategoryIDs.Length == 0)
                continue;

            if (!categoryIDMap.TryGetValue(category.ID, out var newID))
                continue;

            var mappedParentIDs = RemapIDs(category.ParentCategoryIDs, categoryIDMap);

            await connection.ExecuteAsync
            (
                "UPDATE character_categories SET parent_category_ids = @parentCategoryIDs WHERE id = @id",
                new
                {
                    id                = newID,
                    parentCategoryIDs = mappedParentIDs
                },
                transaction
            );
        }
    }

    private static async Task InsertStateAttributesAsync
    (
        SqliteConnection       connection,
        SqliteTransaction      transaction,
        List<StateAttribute>   attributes,
        long                   newProjectID,
        Dictionary<long, long> categoryIDMap,
        CancellationToken      cancellationToken
    )
    {
        foreach (var attr in attributes)
        {
            var mappedCategoryID = attr.CategoryID.HasValue && categoryIDMap.TryGetValue(attr.CategoryID.Value, out var newCatID) ?
                                       (long?)newCatID :
                                       null;

            await connection.ExecuteAsync
            (
                """
                INSERT INTO state_attributes (project_id, name, display_name, scope, category_id, value_type, driver, config)
                VALUES (@projectID, @name, @displayName, @scope, @categoryID, @valueType, @driver, @config)
                """,
                new
                {
                    projectID   = newProjectID,
                    name        = attr.Name,
                    displayName = attr.DisplayName,
                    scope       = attr.Scope,
                    categoryID  = mappedCategoryID,
                    valueType   = attr.ValueType,
                    driver      = attr.Driver,
                    config      = attr.Config
                },
                transaction
            );
        }
    }

    private static async Task<Dictionary<long, long>> InsertKnowledgeGroupsAsync
    (
        SqliteConnection     connection,
        SqliteTransaction    transaction,
        List<KnowledgeGroup> groups,
        long                 newProjectID,
        CancellationToken    cancellationToken
    )
    {
        var idMap = new Dictionary<long, long>();

        foreach (var group in groups)
        {
            var id = await connection.ExecuteScalarAsync<long>
                     (
                         """
                         INSERT INTO knowledge_groups (project_id, name, description, active)
                         VALUES (@projectID, @name, @description, @active);
                         SELECT last_insert_rowid();
                         """,
                         new
                         {
                             projectID   = newProjectID,
                             name        = group.Name,
                             description = group.Description,
                             active      = group.Active
                         },
                         transaction
                     );

            idMap[group.ID] = id;
        }

        return idMap;
    }

    private static async Task<Dictionary<long, long>> InsertKnowledgeEntriesAsync
    (
        SqliteConnection       connection,
        SqliteTransaction      transaction,
        List<KnowledgeEntry>   entries,
        long                   newProjectID,
        Dictionary<long, long> groupIDMap,
        CancellationToken      cancellationToken
    )
    {
        var idMap = new Dictionary<long, long>();

        foreach (var entry in entries)
        {
            var mappedGroupID = entry.GroupID.HasValue && groupIDMap.TryGetValue(entry.GroupID.Value, out var newGroupID) ?
                                    (long?)newGroupID :
                                    null;

            var id = await connection.ExecuteScalarAsync<long>
                     (
                         """
                         INSERT INTO knowledge_entries (project_id, remarks, content, keywords, group_id, active, created_at, updated_at)
                         VALUES (@projectID, @remarks, @content, @keywords, @groupID, @active, @createdAt, @updatedAt);
                         SELECT last_insert_rowid();
                         """,
                         new
                         {
                             projectID = newProjectID,
                             remarks   = entry.Remarks,
                             content   = entry.Content,
                             keywords  = entry.Keywords,
                             groupID   = mappedGroupID,
                             active    = entry.Active,
                             createdAt = entry.CreatedAt,
                             updatedAt = entry.UpdatedAt
                         },
                         transaction
                     );

            idMap[entry.ID] = id;
        }

        return idMap;
    }

    private static async Task InsertKnowledgeEntityIndexAsync
    (
        SqliteConnection           connection,
        SqliteTransaction          transaction,
        List<KnowledgeEntityIndex> entityIndex,
        Dictionary<long, long>     entryIDMap,
        CancellationToken          cancellationToken
    )
    {
        foreach (var idx in entityIndex)
        {
            if (!entryIDMap.TryGetValue(idx.EntryID, out var newEntryID))
                continue;

            await connection.ExecuteAsync
            (
                """
                INSERT OR IGNORE INTO knowledge_entity_index (entry_id, entity_name)
                VALUES (@entryID, @entityName)
                """,
                new
                {
                    entryID    = newEntryID,
                    entityName = idx.EntityName
                },
                transaction
            );
        }
    }

    public async Task<ProjectImportResult> ImportSillyTavernAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);

        var card = await JsonSerializer.DeserializeAsync<SillyTavernCard>(stream, JsonOptions.Default, cancellationToken) ??
                   throw new InvalidDataException("无效的 SillyTavern 角色卡: JSON 解析失败");

        var data = card.Data ?? card;

        var name           = data.Name;
        var description    = BuildDescription(data.SystemPrompt, data.Description);
        var openingMessage = data.FirstMes;

        var project = new Project
        {
            Name           = name,
            Description    = description,
            OpeningMessage = openingMessage
        };

        var groups  = new List<KnowledgeGroup>();
        var entries = new List<KnowledgeEntry>();

        var book = data.CharacterBook;

        if (book?.Entries is { Count: > 0 } bookEntries)
        {
            const long TEMP_GROUP_ID = 1;

            groups.Add
            (
                new KnowledgeGroup
                {
                    ID     = TEMP_GROUP_ID,
                    Name   = localizationService.Get("Project.Import.SillyTavern.DefaultGroupName"),
                    Active = true
                }
            );

            for (var i = 0; i < bookEntries.Count; i++)
            {
                var entry = bookEntries[i];

                entries.Add
                (
                    new KnowledgeEntry
                    {
                        ID       = i + 1,
                        GroupID  = TEMP_GROUP_ID,
                        Remarks  = entry.Comment ?? string.Empty,
                        Content  = entry.Content ?? string.Empty,
                        Keywords = [..entry.Keys, ..entry.SecondaryKeys],
                        Active   = entry.Enabled
                    }
                );
            }
        }

        await using var connection  = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var now          = DateTime.UtcNow;
            var newProjectID = await InsertProjectAsync(connection, transaction, project, now, cancellationToken);

            var groupIDMap = await InsertKnowledgeGroupsAsync
                             (
                                 connection,
                                 transaction,
                                 groups,
                                 newProjectID,
                                 cancellationToken
                             );

            await InsertKnowledgeEntriesAsync
            (
                connection,
                transaction,
                entries,
                newProjectID,
                groupIDMap,
                cancellationToken
            );

            await transaction.CommitAsync(cancellationToken);

            return new ProjectImportResult
            {
                ProjectID           = newProjectID,
                ProjectName         = project.Name,
                KnowledgeEntryCount = entries.Count
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string BuildDescription(string systemPrompt, string description)
    {
        var hasSystem = !string.IsNullOrWhiteSpace(systemPrompt);
        var hasDesc   = !string.IsNullOrWhiteSpace(description);

        if (hasSystem && hasDesc)
            return systemPrompt + "\n\n" + description;

        if (hasSystem)
            return systemPrompt;

        return description ?? string.Empty;
    }

    private static long[] RemapIDs(long[] ids, Dictionary<long, long> idMap)
    {
        var result = new long[ids.Length];

        for (var i = 0; i < ids.Length; i++)
            result[i] = idMap.TryGetValue(ids[i], out var newID) ?
                            newID :
                            0;

        return result;
    }

    private sealed class PackageManifest
    {
        public string Format { get; set; } = string.Empty;

        public int Version { get; set; }

        public DateTime ExportedAt { get; set; }

        public string ProjectName { get; set; } = string.Empty;
    }

    private sealed class ProjectPackageData
    {
        public Project? Project { get; set; }

        public List<CharacterCategory> CharacterCategories { get; set; } = [];

        public List<StateAttribute> StateAttributes { get; set; } = [];

        public List<KnowledgeGroup> KnowledgeGroups { get; set; } = [];

        public List<KnowledgeEntry> KnowledgeEntries { get; set; } = [];

        public List<KnowledgeEntityIndex> KnowledgeEntityIndex { get; set; } = [];
    }

    private sealed class SillyTavernCard : SillyTavernCardData
    {
        public SillyTavernCardData? Data { get; set; }
    }

    private class SillyTavernCardData
    {
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("first_mes")]
        public string FirstMes { get; set; } = string.Empty;

        [JsonPropertyName("system_prompt")]
        public string SystemPrompt { get; set; } = string.Empty;

        [JsonPropertyName("character_book")]
        public SillyTavernCharacterBook? CharacterBook { get; set; }
    }

    private sealed class SillyTavernCharacterBook
    {
        public List<SillyTavernBookEntry> Entries { get; set; } = [];
    }

    private sealed class SillyTavernBookEntry
    {
        public List<string> Keys { get; set; } = [];

        [JsonPropertyName("secondary_keys")]
        public List<string> SecondaryKeys { get; set; } = [];

        public string Comment { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;
    }
}
