using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Services;
using Microsoft.Data.Sqlite;

namespace DirectorPrompt.Infrastructure;

public sealed class ProjectPortService
(
    SqliteConnectionFactory connectionFactory
) : IProjectPortService
{
    private const string PACKAGE_FORMAT = "DirectorPrompt-Project-Package";

    private const int PACKAGE_VERSION = 1;

    private static readonly JsonSerializerOptions JSONOptions = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        Converters                  = { new JsonStringEnumConverter() }
    };

    public async Task ExportAsync(long projectID, string filePath, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

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
            await JsonSerializer.SerializeAsync(manifestStream, manifest, JSONOptions, cancellationToken);

        var dataEntry = zip.CreateEntry("project.json");

        await using (var dataStream = dataEntry.Open())
            await JsonSerializer.SerializeAsync(dataStream, packageData, JSONOptions, cancellationToken);
    }

    public async Task<ProjectImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var zip = ZipFile.OpenRead(filePath);

        var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("无效的项目包: 缺少 manifest.json");

        PackageManifest manifest;

        using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<PackageManifest>(manifestStream, JSONOptions, cancellationToken) ??
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
            data = await JsonSerializer.DeserializeAsync<ProjectPackageData>(dataStream, JSONOptions, cancellationToken) ??
                   throw new InvalidDataException("无效的项目包: project.json 解析失败");
        }

        if (data.Project is null)
            throw new InvalidDataException("无效的项目包: 缺少项目数据");

        await using var connection  = await connectionFactory.CreateAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var now          = DateTime.UtcNow.ToString("O");
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
    )
    {
        var row = await connection.QueryFirstOrDefaultAsync<ProjectRow>
                  (
                      "SELECT * FROM projects WHERE id = @id",
                      new { id = projectID }
                  );

        return row?.ToProject();
    }

    private static async Task<List<CharacterCategory>> QueryCharacterCategoriesAsync
    (
        SqliteConnection  connection,
        long              projectID,
        CancellationToken cancellationToken
    )
    {
        var rows = await connection.QueryAsync<CharacterCategoryRow>
                   (
                       "SELECT * FROM character_categories WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.Select(r => r.ToCharacterCategory()).ToList();
    }

    private static async Task<List<StateAttribute>> QueryStateAttributesAsync
    (
        SqliteConnection  connection,
        long              projectID,
        CancellationToken cancellationToken
    )
    {
        var rows = await connection.QueryAsync<StateAttributeRow>
                   (
                       "SELECT * FROM state_attributes WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.Select(r => r.ToStateAttribute()).ToList();
    }

    private static async Task<List<KnowledgeGroup>> QueryKnowledgeGroupsAsync
    (
        SqliteConnection  connection,
        long              projectID,
        CancellationToken cancellationToken
    )
    {
        var rows = await connection.QueryAsync<KnowledgeGroupRow>
                   (
                       "SELECT * FROM knowledge_groups WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.Select(r => r.ToKnowledgeGroup()).ToList();
    }

    private static async Task<List<KnowledgeEntry>> QueryKnowledgeEntriesAsync
    (
        SqliteConnection  connection,
        long              projectID,
        CancellationToken cancellationToken
    )
    {
        var rows = await connection.QueryAsync<KnowledgeEntryRow>
                   (
                       "SELECT * FROM knowledge_entries WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.Select(r => r.ToKnowledgeEntry()).ToList();
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
        string            now,
        CancellationToken cancellationToken
    )
    {
        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO projects (name, description, opening_message, memory_config, knowledge_config, created_at, updated_at)
                     VALUES (@name, @description, @openingMessage, @memoryConfig, @knowledgeConfig, @createdAt, @updatedAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         name            = project.Name,
                         description     = project.Description,
                         openingMessage  = project.OpeningMessage,
                         memoryConfig    = project.MemoryConfig,
                         knowledgeConfig = project.KnowledgeConfig,
                         createdAt       = now,
                         updatedAt       = now
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
                    parentCategoryIDs = JsonHelper.Serialize(mappedParentIDs)
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
                    scope       = attr.Scope.ToString().ToLowerInvariant(),
                    categoryID  = mappedCategoryID,
                    valueType   = attr.ValueType.ToString().ToLowerInvariant(),
                    driver      = attr.Driver.ToString().ToLowerInvariant(),
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
                             active = group.Active ?
                                          1 :
                                          0
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
                             keywords  = JsonHelper.Serialize(entry.Keywords),
                             groupID   = mappedGroupID,
                             active = entry.Active ?
                                          1 :
                                          0,
                             createdAt = entry.CreatedAt.ToString("O"),
                             updatedAt = entry.UpdatedAt.ToString("O")
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

    private sealed class ProjectRow
    {
        public long ID { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Opening_Message { get; set; } = string.Empty;

        public string Memory_Config { get; set; } = "{}";

        public string Knowledge_Config { get; set; } = "{}";

        public string Created_At { get; set; } = string.Empty;

        public string Updated_At { get; set; } = string.Empty;

        public Project ToProject() =>
            new()
            {
                ID              = ID,
                Name            = Name,
                Description     = Description,
                OpeningMessage  = Opening_Message,
                MemoryConfig    = Memory_Config,
                KnowledgeConfig = Knowledge_Config,
                CreatedAt       = DateTime.Parse(Created_At),
                UpdatedAt       = DateTime.Parse(Updated_At)
            };
    }

    private sealed class CharacterCategoryRow
    {
        public long ID { get; set; }

        public long Project_ID { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string Parent_Category_IDs { get; set; } = "[]";

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

    private sealed class StateAttributeRow
    {
        public long ID { get; set; }

        public long Project_ID { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Display_Name { get; set; } = string.Empty;

        public string Scope { get; set; } = "global";

        public long? Category_ID { get; set; }

        public string Value_Type { get; set; } = "numeric";

        public string Driver { get; set; } = "narrative";

        public string Config { get; set; } = "{}";

        public StateAttribute ToStateAttribute() =>
            new()
            {
                ID          = ID,
                ProjectID   = Project_ID,
                Name        = Name,
                DisplayName = Display_Name,
                Scope = Scope == "category" ?
                            StateScope.Category :
                            StateScope.Global,
                CategoryID = Category_ID,
                ValueType = Value_Type switch
                {
                    "enum"      => StateValueType.Enum,
                    "composite" => StateValueType.Composite,
                    _           => StateValueType.Numeric
                },
                Driver = Driver switch
                {
                    "system" => Domain.Enums.Driver.System,
                    _        => Domain.Enums.Driver.Narrative
                },
                Config = Config
            };
    }

    private sealed class KnowledgeGroupRow
    {
        public long ID { get; set; }

        public long Project_ID { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public int Active { get; set; }

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

    private sealed class KnowledgeEntryRow
    {
        public long ID { get; set; }

        public long Project_ID { get; set; }

        public string Remarks { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public string Keywords { get; set; } = "[]";

        public long? Group_ID { get; set; }

        public int Active { get; set; }

        public string? Content_Hash { get; set; }

        public string Created_At { get; set; } = string.Empty;

        public string Updated_At { get; set; } = string.Empty;

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
}
