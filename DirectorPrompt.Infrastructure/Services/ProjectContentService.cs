using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using DirectorPrompt.Infrastructure;
using Microsoft.Data.Sqlite;

namespace DirectorPrompt.Infrastructure.Services;

public sealed class ProjectContentService
(
    SQLiteDatabaseScheduler scheduler,
    IProjectRepository      projectRepository
) : IProjectContentService
{
    private readonly Lock                   changeSync     = new();
    private readonly Dictionary<long, bool> pendingChanges = [];

    private int changeBatchDepth;

    public event Action<ProjectContentChange>? Changed;

    public IDisposable BeginChangeBatch()
    {
        lock (changeSync)
        {
            changeBatchDepth++;
        }

        return new ChangeBatch(this);
    }

    public void NotifyProjectChanged(long projectID, bool isDeleted = false) =>
        NotifyChanged(projectID, isDeleted);

    public Task<IReadOnlyList<Project>> ListProjectsAsync(CancellationToken cancellationToken = default) =>
        projectRepository.GetAllAsync(cancellationToken);

    public Task<ProjectContentSnapshot?> GetProjectAsync(long projectID, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteReadAsync
        (
            async (connection, token) =>
            {
                var project = await connection.QueryFirstOrDefaultAsync<Project>
                              (
                                  new CommandDefinition
                                  (
                                      "SELECT * FROM projects WHERE id = @projectID",
                                      new { projectID },
                                      cancellationToken: token
                                  )
                              );

                if (project is null)
                    return null;

                var groups = (await connection.QueryAsync<KnowledgeGroup>
                              (
                                  new CommandDefinition
                                  (
                                      "SELECT * FROM knowledge_groups WHERE project_id = @projectID ORDER BY id",
                                      new { projectID },
                                      cancellationToken: token
                                  )
                              )).ToList();
                var entries = (await connection.QueryAsync<KnowledgeEntry>
                               (
                                   new CommandDefinition
                                   (
                                       "SELECT * FROM knowledge_entries WHERE project_id = @projectID ORDER BY id",
                                       new { projectID },
                                       cancellationToken: token
                                   )
                               )).ToList();
                var categories = (await connection.QueryAsync<CharacterCategory>
                                  (
                                      new CommandDefinition
                                      (
                                          "SELECT * FROM character_categories WHERE project_id = @projectID ORDER BY id",
                                          new { projectID },
                                          cancellationToken: token
                                      )
                                  )).ToList();
                var attributes = (await connection.QueryAsync<StateAttribute>
                                  (
                                      new CommandDefinition
                                      (
                                          "SELECT * FROM state_attributes WHERE project_id = @projectID ORDER BY id",
                                          new { projectID },
                                          cancellationToken: token
                                      )
                                  )).ToList();
                var grouped = groups.Select
                                    (group => new ProjectKnowledgeGroup
                                     (
                                         group,
                                         entries.Where(entry => entry.GroupID == group.ID).ToList()
                                     )
                                    )
                                    .ToList();
                var states = attributes.Select(ToProjectStateAttribute).ToList();

                return new ProjectContentSnapshot
                (
                    project,
                    grouped,
                    entries.Where(entry => entry.GroupID is null).ToList(),
                    categories,
                    states
                );
            },
            cancellationToken: cancellationToken
        );

    public async Task<ProjectBlueprintResult> CreateProjectAsync
    (
        string            name,
        string            description,
        string            openingMessage,
        ProjectBlueprint? blueprint,
        bool              dryRun,
        CancellationToken cancellationToken = default
    )
    {
        ValidateProjectName(name);
        blueprint ??= new ProjectBlueprint();
        ValidateBlueprint(blueprint);

        if (dryRun)
        {
            return new ProjectBlueprintResult
            (
                new Project { Name = name.Trim(), Description = description, OpeningMessage = openingMessage },
                new Dictionary<string, long>(),
                new Dictionary<string, long>(),
                new Dictionary<string, long>()
            );
        }

        var result = await scheduler.ExecuteAsync
                     (
                         async (connection, token) =>
                         {
                             await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);

                             try
                             {
                                 var now = DateTime.UtcNow;
                                 var projectID = await connection.ExecuteScalarAsync<long>
                                                 (
                                                     new CommandDefinition
                                                     (
                                                         """
                                                         INSERT INTO projects (name, description, opening_message, created_at, updated_at)
                                                         VALUES (@name, @description, @openingMessage, @createdAt, @updatedAt);
                                                         SELECT last_insert_rowid();
                                                         """,
                                                         new
                                                         {
                                                             name = name.Trim(),
                                                             description,
                                                             openingMessage,
                                                             createdAt = now,
                                                             updatedAt = now
                                                         },
                                                         transaction,
                                                         cancellationToken: token
                                                     )
                                                 );
                                 var categoryIDs = await InsertCategoriesAsync
                                                   (
                                                       connection,
                                                       transaction,
                                                       projectID,
                                                       blueprint.CharacterCategories,
                                                       token
                                                   );
                                 var groupIDs = await InsertKnowledgeGroupsAsync
                                                (
                                                    connection,
                                                    transaction,
                                                    projectID,
                                                    blueprint.KnowledgeGroups,
                                                    token
                                                );
                                 var entryIDs = await InsertKnowledgeEntriesAsync
                                                (
                                                    connection,
                                                    transaction,
                                                    projectID,
                                                    blueprint.KnowledgeGroups,
                                                    groupIDs,
                                                    token
                                                );

                                 await InsertStateAttributesAsync
                                 (
                                     connection,
                                     transaction,
                                     projectID,
                                     blueprint.StateAttributes,
                                     null,
                                     categoryIDs,
                                     groupIDs,
                                     entryIDs,
                                     token
                                 );

                                 foreach (var category in blueprint.CharacterCategories)
                                 {
                                     if (!categoryIDs.TryGetValue(category.Key, out var categoryID))
                                         continue;

                                     await InsertStateAttributesAsync
                                     (
                                         connection,
                                         transaction,
                                         projectID,
                                         category.StateAttributes,
                                         categoryID,
                                         categoryIDs,
                                         groupIDs,
                                         entryIDs,
                                         token
                                     );
                                 }

                                 await transaction.CommitAsync(token);

                                 return new ProjectBlueprintResult
                                 (
                                     new Project
                                     {
                                         ID             = projectID,
                                         Name           = name.Trim(),
                                         Description    = description,
                                         OpeningMessage = openingMessage,
                                         CreatedAt      = now,
                                         UpdatedAt      = now
                                     },
                                     categoryIDs,
                                     groupIDs,
                                     entryIDs
                                 );
                             }
                             catch
                             {
                                 await transaction.RollbackAsync(token);
                                 throw;
                             }
                         },
                         cancellationToken: cancellationToken
                     );

        NotifyChanged(result.Project.ID, false);
        return result;
    }

    public async Task<Project> UpdateProjectAsync(Project project, CancellationToken cancellationToken = default)
    {
        ValidateProjectName(project.Name);
        await EnsureProjectExistsAsync(project.ID, cancellationToken);
        await projectRepository.UpdateAsync(project with { Name = project.Name.Trim() }, cancellationToken);
        var updated = await projectRepository.GetByIDAsync(project.ID, cancellationToken) ??
                      throw new InvalidOperationException($"项目不存在: ID={project.ID}");

        NotifyChanged(project.ID, false);
        return updated;
    }

    public async Task<Project> PatchProjectAsync
    (
        long              projectID,
        ProjectPatch      patch,
        CancellationToken cancellationToken = default
    )
    {
        if (patch.Name is null && patch.Description is null && patch.OpeningMessage is null)
            throw new ArgumentException("至少提供一个要更新的项目字段", nameof(patch));

        var snapshot = await GetProjectAsync(projectID, cancellationToken) ??
                       throw new InvalidOperationException($"项目不存在: ID={projectID}");

        return await UpdateProjectAsync
        (
            snapshot.Project with
            {
                Name           = patch.Name           ?? snapshot.Project.Name,
                Description    = patch.Description    ?? snapshot.Project.Description,
                OpeningMessage = patch.OpeningMessage ?? snapshot.Project.OpeningMessage
            },
            cancellationToken
        );
    }

    public async Task<ProjectDeleteSummary> DeleteProjectAsync(long projectID, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetProjectAsync(projectID, cancellationToken) ??
                       throw new InvalidOperationException($"项目不存在: ID={projectID}");
        var metrics = CountDependencies(snapshot, new HashSet<string>());

        await projectRepository.DeleteAsync(projectID, cancellationToken);
        NotifyChanged(projectID, true);

        return new ProjectDeleteSummary
        (
            1,
            snapshot.KnowledgeGroups.Count,
            snapshot.KnowledgeGroups.Sum(group => group.Entries.Count) + snapshot.UngroupedKnowledgeEntries.Count,
            snapshot.CharacterCategories.Count,
            snapshot.StateAttributes.Count,
            metrics.Transitions,
            metrics.PhaseReferences
        );
    }

    public async Task<KnowledgeGroup> ManageKnowledgeGroupAsync
    (
        long                 projectID,
        ProjectContentAction action,
        KnowledgeGroup?      group,
        long?                groupID,
        CancellationToken    cancellationToken = default
    )
    {
        await EnsureProjectExistsAsync(projectID, cancellationToken);

        switch (action)
        {
            case ProjectContentAction.Create:
            {
                var value = group ?? throw new ArgumentException("缺少知识分组数据", nameof(group));

                if (string.IsNullOrWhiteSpace(value.Name))
                    throw new ArgumentException("知识分组名称不能为空", nameof(group));

                var created = await scheduler.ExecuteAsync
                              (
                                  async (connection, token) =>
                                  {
                                      var id = await connection.ExecuteScalarAsync<long>
                                               (
                                                   new CommandDefinition
                                                   (
                                                       """
                                                       INSERT INTO knowledge_groups (project_id, name, description, active)
                                                       VALUES (@projectID, @name, @description, @active);
                                                       SELECT last_insert_rowid();
                                                       """,
                                                       new
                                                       {
                                                           projectID,
                                                           name        = value.Name,
                                                           description = value.Description,
                                                           active      = value.Active
                                                       },
                                                       cancellationToken: token
                                                   )
                                               );

                                      return value with { ID = id, ProjectID = projectID };
                                  },
                                  cancellationToken: cancellationToken
                              );
                NotifyChanged(projectID, false);
                return created;
            }
            case ProjectContentAction.Update:
            {
                var value = group   ?? throw new ArgumentException("缺少知识分组数据", nameof(group));
                var id    = groupID ?? value.ID;
                await EnsureEntityProjectAsync("knowledge_groups", id, projectID, cancellationToken);
                await scheduler.ExecuteAsync
                (
                    (connection, token) => connection.ExecuteAsync
                    (
                        new CommandDefinition
                        (
                            "UPDATE knowledge_groups SET name = @name, description = @description, active = @active WHERE id = @id",
                            new { id, name = value.Name, description = value.Description, active = value.Active },
                            cancellationToken: token
                        )
                    ),
                    cancellationToken: cancellationToken
                );
                NotifyChanged(projectID, false);
                return value with { ID = id, ProjectID = projectID };
            }
            case ProjectContentAction.Delete:
            {
                var id      = groupID ?? throw new ArgumentException("缺少知识分组 ID", nameof(groupID));
                var deleted = await DeleteKnowledgeGroupAsync(projectID, id, cancellationToken);
                NotifyChanged(projectID, false);
                return deleted;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    public async Task<KnowledgeGroup> PatchKnowledgeGroupAsync
    (
        long                projectID,
        long                groupID,
        KnowledgeGroupPatch patch,
        CancellationToken   cancellationToken = default
    )
    {
        var snapshot = await GetProjectAsync(projectID, cancellationToken) ??
                       throw new InvalidOperationException($"项目不存在: ID={projectID}");
        var group = snapshot.KnowledgeGroups.FirstOrDefault(item => item.Group.ID == groupID)?.Group ??
                    throw new InvalidOperationException($"知识分组不存在: ID={groupID}");

        return await ManageKnowledgeGroupAsync
        (
            projectID,
            ProjectContentAction.Update,
            group with
            {
                Name        = patch.Name        ?? group.Name,
                Description = patch.Description ?? group.Description,
                Active      = patch.Active      ?? group.Active
            },
            groupID,
            cancellationToken
        );
    }

    public async Task<KnowledgeEntry> ManageKnowledgeEntryAsync
    (
        long                 projectID,
        ProjectContentAction action,
        KnowledgeEntry?      entry,
        long?                entryID,
        CancellationToken    cancellationToken = default
    )
    {
        await EnsureProjectExistsAsync(projectID, cancellationToken);

        switch (action)
        {
            case ProjectContentAction.Create:
            {
                var value = entry ?? throw new ArgumentException("缺少知识条目数据", nameof(entry));
                await ValidateKnowledgeGroupAsync(projectID, value.GroupID, cancellationToken);
                var now = DateTime.UtcNow;
                var created = await scheduler.ExecuteAsync
                              (
                                  async (connection, token) =>
                                  {
                                      var id = await connection.ExecuteScalarAsync<long>
                                               (
                                                   new CommandDefinition
                                                   (
                                                       """
                                                       INSERT INTO knowledge_entries (project_id, remarks, content, keywords, group_id, active, created_at, updated_at)
                                                       VALUES (@projectID, @remarks, @content, @keywords, @groupID, @active, @createdAt, @updatedAt);
                                                       SELECT last_insert_rowid();
                                                       """,
                                                       new
                                                       {
                                                           projectID,
                                                           remarks   = value.Remarks,
                                                           content   = value.Content,
                                                           keywords  = value.Keywords,
                                                           groupID   = value.GroupID,
                                                           active    = value.Active,
                                                           createdAt = now,
                                                           updatedAt = now
                                                       },
                                                       cancellationToken: token
                                                   )
                                               );

                                      return value with { ID = id, ProjectID = projectID, CreatedAt = now, UpdatedAt = now };
                                  },
                                  cancellationToken: cancellationToken
                              );
                NotifyChanged(projectID, false);
                return created;
            }
            case ProjectContentAction.Update:
            {
                var value = entry   ?? throw new ArgumentException("缺少知识条目数据", nameof(entry));
                var id    = entryID ?? value.ID;
                await EnsureEntityProjectAsync("knowledge_entries", id, projectID, cancellationToken);
                await ValidateKnowledgeGroupAsync(projectID, value.GroupID, cancellationToken);
                var now = DateTime.UtcNow;
                await scheduler.ExecuteAsync
                (
                    (connection, token) => connection.ExecuteAsync
                    (
                        new CommandDefinition
                        (
                            """
                            UPDATE knowledge_entries
                            SET remarks = @remarks,
                                content = @content,
                                keywords = @keywords,
                                group_id = @groupID,
                                active = @active,
                                content_hash = CASE
                                                   WHEN content IS NOT @content OR keywords IS NOT @keywords THEN NULL
                                                   ELSE content_hash
                                               END,
                                embedding_fingerprint = CASE
                                                            WHEN content IS NOT @content OR keywords IS NOT @keywords THEN NULL
                                                            ELSE embedding_fingerprint
                                                        END,
                                updated_at = @updatedAt
                            WHERE id = @id
                            """,
                            new
                            {
                                id,
                                remarks   = value.Remarks,
                                content   = value.Content,
                                keywords  = value.Keywords,
                                groupID   = value.GroupID,
                                active    = value.Active,
                                updatedAt = now
                            },
                            cancellationToken: token
                        )
                    ),
                    cancellationToken: cancellationToken
                );
                NotifyChanged(projectID, false);
                return value with { ID = id, ProjectID = projectID, UpdatedAt = now };
            }
            case ProjectContentAction.Delete:
            {
                var id      = entryID ?? throw new ArgumentException("缺少知识条目 ID", nameof(entryID));
                var deleted = await DeleteKnowledgeEntryAsync(projectID, id, cancellationToken);
                NotifyChanged(projectID, false);
                return deleted;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    public async Task<KnowledgeEntry> PatchKnowledgeEntryAsync
    (
        long                projectID,
        long                entryID,
        KnowledgeEntryPatch patch,
        CancellationToken   cancellationToken = default
    )
    {
        if (patch is { GroupID: not null, MoveToUngrouped: true })
            throw new ArgumentException("知识条目不能同时指定分组和移出分组", nameof(patch));

        var snapshot = await GetProjectAsync(projectID, cancellationToken) ??
                       throw new InvalidOperationException($"项目不存在: ID={projectID}");
        var entry = snapshot.UngroupedKnowledgeEntries
                            .Concat(snapshot.KnowledgeGroups.SelectMany(group => group.Entries))
                            .FirstOrDefault(item => item.ID == entryID) ??
                    throw new InvalidOperationException($"知识条目不存在: ID={entryID}");

        return await ManageKnowledgeEntryAsync
        (
            projectID,
            ProjectContentAction.Update,
            entry with
            {
                Remarks = patch.Remarks ?? entry.Remarks,
                Content  = patch.Content  ?? entry.Content,
                Keywords = patch.Keywords ?? entry.Keywords,
                GroupID  = patch.MoveToUngrouped == true ? null : patch.GroupID ?? entry.GroupID,
                Active   = patch.Active ?? entry.Active
            },
            entryID,
            cancellationToken
        );
    }

    public async Task<CharacterCategory> ManageCharacterCategoryAsync
    (
        long                 projectID,
        ProjectContentAction action,
        CharacterCategory?   category,
        long?                categoryID,
        CancellationToken    cancellationToken = default
    )
    {
        await EnsureProjectExistsAsync(projectID, cancellationToken);

        switch (action)
        {
            case ProjectContentAction.Create:
            {
                var value = category ?? throw new ArgumentException("缺少人物分类数据", nameof(category));
                await ValidateCategoryParentsAsync(projectID, value.ParentCategoryIDs, null, cancellationToken);
                var created = await scheduler.ExecuteAsync
                              (
                                  async (connection, token) =>
                                  {
                                      var id = await connection.ExecuteScalarAsync<long>
                                               (
                                                   new CommandDefinition
                                                   (
                                                       """
                                                       INSERT INTO character_categories (project_id, name, description, parent_category_ids)
                                                       VALUES (@projectID, @name, @description, @parentCategoryIDs);
                                                       SELECT last_insert_rowid();
                                                       """,
                                                       new
                                                       {
                                                           projectID,
                                                           name              = value.Name,
                                                           description       = value.Description,
                                                           parentCategoryIDs = value.ParentCategoryIDs
                                                       },
                                                       cancellationToken: token
                                                   )
                                               );

                                      return value with { ID = id, ProjectID = projectID };
                                  },
                                  cancellationToken: cancellationToken
                              );
                NotifyChanged(projectID, false);
                return created;
            }
            case ProjectContentAction.Update:
            {
                var value = category   ?? throw new ArgumentException("缺少人物分类数据", nameof(category));
                var id    = categoryID ?? value.ID;
                await EnsureEntityProjectAsync("character_categories", id, projectID, cancellationToken);
                await ValidateCategoryParentsAsync(projectID, value.ParentCategoryIDs, id, cancellationToken);
                await scheduler.ExecuteAsync
                (
                    (connection, token) => connection.ExecuteAsync
                    (
                        new CommandDefinition
                        (
                            "UPDATE character_categories SET name = @name, description = @description, parent_category_ids = @parentCategoryIDs WHERE id = @id",
                            new { id, name = value.Name, description = value.Description, parentCategoryIDs = value.ParentCategoryIDs },
                            cancellationToken: token
                        )
                    ),
                    cancellationToken: cancellationToken
                );
                NotifyChanged(projectID, false);
                return value with { ID = id, ProjectID = projectID };
            }
            case ProjectContentAction.Delete:
            {
                var id      = categoryID ?? throw new ArgumentException("缺少人物分类 ID", nameof(categoryID));
                var deleted = await DeleteCharacterCategoryAsync(projectID, id, cancellationToken);
                NotifyChanged(projectID, false);
                return deleted;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    public async Task<CharacterCategory> PatchCharacterCategoryAsync
    (
        long                   projectID,
        long                   categoryID,
        CharacterCategoryPatch patch,
        CancellationToken      cancellationToken = default
    )
    {
        var snapshot = await GetProjectAsync(projectID, cancellationToken) ??
                       throw new InvalidOperationException($"项目不存在: ID={projectID}");
        var category = snapshot.CharacterCategories.FirstOrDefault(item => item.ID == categoryID) ??
                       throw new InvalidOperationException($"人物分类不存在: ID={categoryID}");

        return await ManageCharacterCategoryAsync
        (
            projectID,
            ProjectContentAction.Update,
            category with
            {
                Name              = patch.Name              ?? category.Name,
                Description       = patch.Description       ?? category.Description,
                ParentCategoryIDs = patch.ParentCategoryIDs ?? category.ParentCategoryIDs
            },
            categoryID,
            cancellationToken
        );
    }

    public async Task<StateAttribute> ManageStateAttributeAsync
    (
        long                      projectID,
        ProjectContentAction      action,
        StateAttributeDefinition? definition,
        long?                     attributeID,
        CancellationToken         cancellationToken = default
    )
    {
        await EnsureProjectExistsAsync(projectID, cancellationToken);

        switch (action)
        {
            case ProjectContentAction.Create:
            {
                var value = definition ?? throw new ArgumentException("缺少状态属性数据", nameof(definition));
                await ValidateStateDefinitionAsync(projectID, value, null, cancellationToken);
                var created = await CreateStateAttributeAsync(projectID, value, cancellationToken);
                NotifyChanged(projectID, false);
                return created;
            }
            case ProjectContentAction.Update:
            {
                var value = definition  ?? throw new ArgumentException("缺少状态属性数据",  nameof(definition));
                var id    = attributeID ?? throw new ArgumentException("缺少状态属性 ID", nameof(attributeID));
                await EnsureEntityProjectAsync("state_attributes", id, projectID, cancellationToken);
                await ValidateStateDefinitionAsync(projectID, value, id, cancellationToken);
                var updated = await UpdateStateAttributeAsync(projectID, id, value, cancellationToken);
                NotifyChanged(projectID, false);
                return updated;
            }
            case ProjectContentAction.Delete:
            {
                var id      = attributeID ?? throw new ArgumentException("缺少状态属性 ID", nameof(attributeID));
                var deleted = await DeleteStateAttributeAsync(projectID, id, cancellationToken);
                NotifyChanged(projectID, false);
                return deleted;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    public async Task<StateAttribute> PatchStateAttributeAsync
    (
        long                projectID,
        long                attributeID,
        StateAttributePatch patch,
        CancellationToken   cancellationToken = default
    )
    {
        var snapshot = await GetProjectAsync(projectID, cancellationToken) ??
                       throw new InvalidOperationException($"项目不存在: ID={projectID}");
        var attribute = snapshot.StateAttributes.FirstOrDefault(item => item.ID == attributeID) ??
                        throw new InvalidOperationException($"状态属性不存在: ID={attributeID}");
        var config = attribute.Configuration;
        var trigger = Enum.TryParse<SystemTrigger>(config.Trigger, true, out var parsedTrigger) ?
                          parsedTrigger :
                          SystemTrigger.SceneChange;
        var scope = patch.Scope ?? attribute.Scope;

        return await ManageStateAttributeAsync
        (
            projectID,
            ProjectContentAction.Update,
            new StateAttributeDefinition
            {
                Name        = patch.Name        ?? attribute.Name,
                DisplayName = patch.DisplayName ?? attribute.DisplayName,
                Scope       = scope,
                CategoryID = scope == StateScope.Global ?
                                 null :
                                 patch.CategoryID ?? attribute.CategoryID,
                ValueType   = patch.ValueType   ?? attribute.ValueType,
                Driver      = patch.Driver      ?? attribute.Driver,
                Numeric = patch.Numeric ?? new NumericStateDefinition
                {
                    Min         = config.Min,
                    Max         = config.Max,
                    Initial     = config.Initial,
                    Unit        = config.Unit,
                    ChangeRules = config.ChangeRules,
                    Changes     = config.NumericChanges
                },
                Enumeration = patch.Enumeration ?? new EnumStateDefinition
                {
                    Options     = config.Options ?? [],
                    Trigger     = trigger,
                    Transitions = config.Transitions ?? []
                },
                Phases = patch.Phases ?? config.Phases.Select
                (phase => new PhaseDefinition
                    {
                        Name              = phase.Name,
                        Expression        = phase.Expression,
                        KnowledgeEntryIDs = [.. phase.KnowledgeIDs],
                        KnowledgeGroupIDs = [.. phase.KnowledgeGroupIDs],
                        EnterDirectives   = [.. phase.EnterDirectives],
                        ExitDirectives    = [.. phase.ExitDirectives]
                    }
                ).ToList()
            },
            attributeID,
            cancellationToken
        );
    }

    private async Task<KnowledgeGroup> DeleteKnowledgeGroupAsync(long projectID, long groupID, CancellationToken cancellationToken) =>
        await scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                var group = await connection.QueryFirstOrDefaultAsync<KnowledgeGroup>
                            (
                                new CommandDefinition
                                (
                                    "SELECT * FROM knowledge_groups WHERE id = @groupID AND project_id = @projectID",
                                    new { groupID, projectID },
                                    cancellationToken: token
                                )
                            ) ??
                            throw new InvalidOperationException($"知识分组不存在: ID={groupID}");
                var entryIDs = (await connection.QueryAsync<long>
                                (
                                    new CommandDefinition
                                    (
                                        "SELECT id FROM knowledge_entries WHERE group_id = @groupID",
                                        new { groupID },
                                        cancellationToken: token
                                    )
                                )).ToList();
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);

                try
                {
                    await CleanupConfigurationsAsync
                    (
                        connection,
                        transaction,
                        projectID,
                        entryIDs,
                        [groupID],
                        new HashSet<string>(),
                        token
                    );
                    await DeleteKnowledgeRowsAsync(connection, transaction, projectID, entryIDs, token);
                    await connection.ExecuteAsync
                    (
                        new CommandDefinition
                        (
                            "DELETE FROM knowledge_groups WHERE id = @groupID",
                            new { groupID },
                            transaction,
                            cancellationToken: token
                        )
                    );
                    await transaction.CommitAsync(token);
                    return group;
                }
                catch
                {
                    await transaction.RollbackAsync(token);
                    throw;
                }
            },
            cancellationToken: cancellationToken
        );

    private async Task<KnowledgeEntry> DeleteKnowledgeEntryAsync(long projectID, long entryID, CancellationToken cancellationToken) =>
        await scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                var entry = await connection.QueryFirstOrDefaultAsync<KnowledgeEntry>
                            (
                                new CommandDefinition
                                (
                                    "SELECT * FROM knowledge_entries WHERE id = @entryID AND project_id = @projectID",
                                    new { entryID, projectID },
                                    cancellationToken: token
                                )
                            ) ??
                            throw new InvalidOperationException($"知识条目不存在: ID={entryID}");
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);

                try
                {
                    await CleanupConfigurationsAsync
                    (
                        connection,
                        transaction,
                        projectID,
                        [entryID],
                        [],
                        new HashSet<string>(),
                        token
                    );
                    await DeleteKnowledgeRowsAsync(connection, transaction, projectID, [entryID], token);
                    await transaction.CommitAsync(token);
                    return entry;
                }
                catch
                {
                    await transaction.RollbackAsync(token);
                    throw;
                }
            },
            cancellationToken: cancellationToken
        );

    private async Task<CharacterCategory> DeleteCharacterCategoryAsync(long projectID, long categoryID, CancellationToken cancellationToken) =>
        await scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                var category = await connection.QueryFirstOrDefaultAsync<CharacterCategory>
                               (
                                   new CommandDefinition
                                   (
                                       "SELECT * FROM character_categories WHERE id = @categoryID AND project_id = @projectID",
                                       new { categoryID, projectID },
                                       cancellationToken: token
                                   )
                               ) ??
                               throw new InvalidOperationException($"人物分类不存在: ID={categoryID}");
                var attributes = (await connection.QueryAsync<StateAttribute>
                                  (
                                      new CommandDefinition
                                      (
                                          "SELECT * FROM state_attributes WHERE project_id = @projectID AND category_id = @categoryID",
                                          new { projectID, categoryID },
                                          cancellationToken: token
                                      )
                                  )).ToList();
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);

                try
                {
                    await RemoveCategoryReferencesAsync(connection, transaction, projectID, categoryID, token);
                    await DeleteStateAttributesAsync(connection, transaction, projectID, attributes, token);
                    await connection.ExecuteAsync
                    (
                        new CommandDefinition
                        (
                            "DELETE FROM character_categories WHERE id = @categoryID",
                            new { categoryID },
                            transaction,
                            cancellationToken: token
                        )
                    );
                    await transaction.CommitAsync(token);
                    return category;
                }
                catch
                {
                    await transaction.RollbackAsync(token);
                    throw;
                }
            },
            cancellationToken: cancellationToken
        );

    private async Task<StateAttribute> DeleteStateAttributeAsync(long projectID, long attributeID, CancellationToken cancellationToken) =>
        await scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                var attribute = await connection.QueryFirstOrDefaultAsync<StateAttribute>
                                (
                                    new CommandDefinition
                                    (
                                        "SELECT * FROM state_attributes WHERE id = @attributeID AND project_id = @projectID",
                                        new { attributeID, projectID },
                                        cancellationToken: token
                                    )
                                ) ??
                                throw new InvalidOperationException($"状态属性不存在: ID={attributeID}");
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);

                try
                {
                    await DeleteStateAttributesAsync(connection, transaction, projectID, [attribute], token);
                    await transaction.CommitAsync(token);
                    return attribute;
                }
                catch
                {
                    await transaction.RollbackAsync(token);
                    throw;
                }
            },
            cancellationToken: cancellationToken
        );

    private static async Task<Dictionary<string, long>> InsertCategoriesAsync
    (
        SqliteConnection                           connection,
        SqliteTransaction                          transaction,
        long                                       projectID,
        IReadOnlyList<CharacterCategoryDefinition> categories,
        CancellationToken                          cancellationToken
    )
    {
        var ids = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var category in categories)
        {
            var id = await connection.ExecuteScalarAsync<long>
                     (
                         new CommandDefinition
                         (
                             """
                             INSERT INTO character_categories (project_id, name, description, parent_category_ids)
                             VALUES (@projectID, @name, @description, '[]');
                             SELECT last_insert_rowid();
                             """,
                             new { projectID, name = category.Name, description = category.Description },
                             transaction,
                             cancellationToken: cancellationToken
                         )
                     );
            ids[category.Key] = id;
        }

        foreach (var category in categories)
        {
            if (!ids.TryGetValue(category.Key, out var id))
                continue;

            var parentIDs = category.ParentCategoryKeys.Select(key => ids[key]).ToArray();
            await connection.ExecuteAsync
            (
                new CommandDefinition
                (
                    "UPDATE character_categories SET parent_category_ids = @parentIDs WHERE id = @id",
                    new { id, parentIDs },
                    transaction,
                    cancellationToken: cancellationToken
                )
            );
        }

        return ids;
    }

    private static async Task<Dictionary<string, long>> InsertKnowledgeGroupsAsync
    (
        SqliteConnection                        connection,
        SqliteTransaction                       transaction,
        long                                    projectID,
        IReadOnlyList<KnowledgeGroupDefinition> groups,
        CancellationToken                       cancellationToken
    )
    {
        var ids = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var id = await connection.ExecuteScalarAsync<long>
                     (
                         new CommandDefinition
                         (
                             """
                             INSERT INTO knowledge_groups (project_id, name, description, active)
                             VALUES (@projectID, @name, @description, @active);
                             SELECT last_insert_rowid();
                             """,
                             new { projectID, name = group.Name, description = group.Description, active = group.Active },
                             transaction,
                             cancellationToken: cancellationToken
                         )
                     );
            ids[group.Key] = id;
        }

        return ids;
    }

    private static async Task<Dictionary<string, long>> InsertKnowledgeEntriesAsync
    (
        SqliteConnection                        connection,
        SqliteTransaction                       transaction,
        long                                    projectID,
        IReadOnlyList<KnowledgeGroupDefinition> groups,
        IReadOnlyDictionary<string, long>       groupIDs,
        CancellationToken                       cancellationToken
    )
    {
        var ids = new Dictionary<string, long>(StringComparer.Ordinal);
        var now = DateTime.UtcNow;

        foreach (var group in groups)
        {
            var groupID = groupIDs[group.Key];

            foreach (var entry in group.Entries)
            {
                var id = await connection.ExecuteScalarAsync<long>
                         (
                             new CommandDefinition
                             (
                                 """
                                 INSERT INTO knowledge_entries (project_id, remarks, content, keywords, group_id, active, created_at, updated_at)
                                 VALUES (@projectID, @remarks, @content, @keywords, @groupID, @active, @createdAt, @updatedAt);
                                 SELECT last_insert_rowid();
                                 """,
                                 new
                                 {
                                     projectID,
                                     remarks  = entry.Remarks,
                                     content  = entry.Content,
                                     keywords = entry.Keywords.ToArray(),
                                     groupID,
                                     active    = entry.Active,
                                     createdAt = now,
                                     updatedAt = now
                                 },
                                 transaction,
                                 cancellationToken: cancellationToken
                             )
                         );
                ids[entry.Key] = id;
            }
        }

        return ids;
    }

    private static async Task InsertStateAttributesAsync
    (
        SqliteConnection                        connection,
        SqliteTransaction                       transaction,
        long                                    projectID,
        IReadOnlyList<StateAttributeDefinition> definitions,
        long?                                   categoryID,
        IReadOnlyDictionary<string, long>       categoryIDs,
        IReadOnlyDictionary<string, long>       groupIDs,
        IReadOnlyDictionary<string, long>       entryIDs,
        CancellationToken                       cancellationToken
    )
    {
        foreach (var definition in definitions)
        {
            var scope = categoryID.HasValue ?
                            StateScope.Category :
                            definition.Scope;
            var resolvedCategoryID = categoryID ?? definition.CategoryID;
            var config             = JsonSerializer.Serialize(BuildConfig(definition, groupIDs, entryIDs), JsonOptions.Compact);
            var driver = definition.Driver;

            await connection.ExecuteAsync
            (
                new CommandDefinition
                (
                    """
                    INSERT INTO state_attributes (project_id, name, display_name, scope, category_id, value_type, driver, config)
                    VALUES (@projectID, @name, @displayName, @scope, @categoryID, @valueType, @driver, @config)
                    """,
                    new
                    {
                        projectID,
                        name        = definition.Name,
                        displayName = definition.DisplayName,
                        scope,
                        categoryID = resolvedCategoryID,
                        valueType  = definition.ValueType,
                        driver,
                        config
                    },
                    transaction,
                    cancellationToken: cancellationToken
                )
            );
        }
    }

    private async Task<StateAttribute> CreateStateAttributeAsync
    (
        long                     projectID,
        StateAttributeDefinition definition,
        CancellationToken        cancellationToken
    )
    {
        var config = JsonSerializer.Serialize
        (
            BuildConfig
            (
                definition,
                new Dictionary<string, long>(),
                new Dictionary<string, long>()
            ),
            JsonOptions.Compact
        );
        var driver = definition.Driver;

        return await scheduler.ExecuteAsync
               (
                   async (connection, token) =>
                   {
                       var id = await connection.ExecuteScalarAsync<long>
                                (
                                    new CommandDefinition
                                    (
                                        """
                                        INSERT INTO state_attributes (project_id, name, display_name, scope, category_id, value_type, driver, config)
                                        VALUES (@projectID, @name, @displayName, @scope, @categoryID, @valueType, @driver, @config);
                                        SELECT last_insert_rowid();
                                        """,
                                        new
                                        {
                                            projectID,
                                            name        = definition.Name,
                                            displayName = definition.DisplayName,
                                            scope       = definition.Scope,
                                            categoryID = definition.Scope == StateScope.Category ?
                                                             definition.CategoryID :
                                                             null,
                                            valueType = definition.ValueType,
                                            driver,
                                            config
                                        },
                                        cancellationToken: token
                                    )
                                );

                       return new StateAttribute
                       {
                           ID          = id,
                           ProjectID   = projectID,
                           Name        = definition.Name,
                           DisplayName = definition.DisplayName,
                           Scope       = definition.Scope,
                           CategoryID = definition.Scope == StateScope.Category ?
                                            definition.CategoryID :
                                            null,
                           ValueType = definition.ValueType,
                           Driver    = driver,
                           Config    = config
                       };
                   },
                   cancellationToken: cancellationToken
               );
    }

    private async Task<StateAttribute> UpdateStateAttributeAsync
    (
        long                     projectID,
        long                     attributeID,
        StateAttributeDefinition definition,
        CancellationToken        cancellationToken
    )
    {
        var config = JsonSerializer.Serialize
        (
            BuildConfig
            (
                definition,
                new Dictionary<string, long>(),
                new Dictionary<string, long>()
            ),
            JsonOptions.Compact
        );
        var driver = definition.Driver;

        return await scheduler.ExecuteAsync
               (
                   async (connection, token) =>
                   {
                       await connection.ExecuteAsync
                       (
                           new CommandDefinition
                           (
                               """
                               UPDATE state_attributes
                               SET name = @name,
                                   display_name = @displayName,
                                   scope = @scope,
                                   category_id = @categoryID,
                                   value_type = @valueType,
                                   driver = @driver,
                                   config = @config
                               WHERE id = @attributeID
                               """,
                               new
                               {
                                   attributeID,
                                   name        = definition.Name,
                                   displayName = definition.DisplayName,
                                   scope       = definition.Scope,
                                   categoryID = definition.Scope == StateScope.Category ?
                                                    definition.CategoryID :
                                                    null,
                                   valueType = definition.ValueType,
                                   driver,
                                   config
                               },
                               cancellationToken: token
                           )
                       );

                       return new StateAttribute
                       {
                           ID          = attributeID,
                           ProjectID   = projectID,
                           Name        = definition.Name,
                           DisplayName = definition.DisplayName,
                           Scope       = definition.Scope,
                           CategoryID = definition.Scope == StateScope.Category ?
                                            definition.CategoryID :
                                            null,
                           ValueType = definition.ValueType,
                           Driver    = driver,
                           Config    = config
                       };
                   },
                   cancellationToken: cancellationToken
               );
    }

    private static async Task DeleteKnowledgeRowsAsync
    (
        SqliteConnection    connection,
        SqliteTransaction   transaction,
        long                projectID,
        IReadOnlyList<long> entryIDs,
        CancellationToken   cancellationToken
    )
    {
        if (entryIDs.Count == 0)
            return;

        var tableName = VectorTableManager.GetKnowledgeTableName(projectID);

        if (await VectorTableManager.TableExistsAsync(connection, tableName, cancellationToken))
        {
            await connection.ExecuteAsync
            (
                new CommandDefinition
                (
                    $"DELETE FROM \"{tableName}\" WHERE entry_id IN @entryIDs",
                    new { entryIDs },
                    transaction,
                    cancellationToken: cancellationToken
                )
            );
        }

        await connection.ExecuteAsync
        (
            new CommandDefinition
            (
                "DELETE FROM knowledge_entity_index WHERE entry_id IN @entryIDs",
                new { entryIDs },
                transaction,
                cancellationToken: cancellationToken
            )
        );
        await connection.ExecuteAsync
        (
            new CommandDefinition
            (
                "DELETE FROM knowledge_entries WHERE id IN @entryIDs",
                new { entryIDs },
                transaction,
                cancellationToken: cancellationToken
            )
        );
    }

    private static async Task DeleteStateAttributesAsync
    (
        SqliteConnection              connection,
        SqliteTransaction             transaction,
        long                          projectID,
        IReadOnlyList<StateAttribute> attributes,
        CancellationToken             cancellationToken
    )
    {
        if (attributes.Count == 0)
            return;

        var ids   = attributes.Select(attribute => attribute.ID).ToList();
        var names = attributes.Select(attribute => attribute.Name).ToHashSet(StringComparer.Ordinal);
        await CleanupConfigurationsAsync(connection, transaction, projectID, [], [], names, cancellationToken, ids);

        await connection.ExecuteAsync
        (
            new CommandDefinition
            (
                "DELETE FROM character_state_values WHERE attribute_id IN @ids",
                new { ids },
                transaction,
                cancellationToken: cancellationToken
            )
        );
        await connection.ExecuteAsync
        (
            new CommandDefinition
            (
                "DELETE FROM state_values WHERE attribute_id IN @ids",
                new { ids },
                transaction,
                cancellationToken: cancellationToken
            )
        );
        await connection.ExecuteAsync
        (
            new CommandDefinition
            (
                "DELETE FROM state_change_logs WHERE attribute_id IN @ids",
                new { ids },
                transaction,
                cancellationToken: cancellationToken
            )
        );
        await connection.ExecuteAsync
        (
            new CommandDefinition
            (
                "DELETE FROM state_attributes WHERE id IN @ids",
                new { ids },
                transaction,
                cancellationToken: cancellationToken
            )
        );
        await connection.ExecuteAsync
        (
            new CommandDefinition
            (
                "DELETE FROM character_category_resolutions WHERE character_id IN (SELECT id FROM characters WHERE project_id = @projectID)",
                new { projectID },
                transaction,
                cancellationToken: cancellationToken
            )
        );
    }

    private static async Task CleanupConfigurationsAsync
    (
        SqliteConnection     connection,
        SqliteTransaction    transaction,
        long                 projectID,
        IReadOnlyList<long>  entryIDs,
        IReadOnlyList<long>  groupIDs,
        IReadOnlySet<string> attributeNames,
        CancellationToken    cancellationToken,
        IReadOnlyList<long>? excludedAttributeIDs = null
    )
    {
        var attributes = (await connection.QueryAsync<StateAttribute>
                          (
                              new CommandDefinition
                              (
                                  "SELECT * FROM state_attributes WHERE project_id = @projectID",
                                  new { projectID },
                                  transaction,
                                  cancellationToken: cancellationToken
                              )
                          )).ToList();
        var excluded = excludedAttributeIDs?.ToHashSet() ?? [];
        var entrySet = entryIDs.ToHashSet();
        var groupSet = groupIDs.ToHashSet();

        foreach (var attribute in attributes)
        {
            if (excluded.Contains(attribute.ID))
                continue;

            var config = ParseConfig(attribute.Config);
            var phases = config.Phases.Select
            (phase => phase with
                {
                    KnowledgeIDs = phase.KnowledgeIDs.Where(id => !entrySet.Contains(id)).ToList(),
                    KnowledgeGroupIDs = phase.KnowledgeGroupIDs.Where(id => !groupSet.Contains(id)).ToList()
                }
            ).ToList();
            var transitions = config.Transitions?.Where(transition => !attributeNames.Contains(transition.AttributeName ?? string.Empty)).ToList();
            var updated     = config with { Phases = phases, Transitions = transitions };

            if (JsonSerializer.Serialize(updated, JsonOptions.Compact) == JsonSerializer.Serialize(config, JsonOptions.Compact))
                continue;

            await connection.ExecuteAsync
            (
                new CommandDefinition
                (
                    "UPDATE state_attributes SET config = @config WHERE id = @id",
                    new { id = attribute.ID, config = JsonSerializer.Serialize(updated, JsonOptions.Compact) },
                    transaction,
                    cancellationToken: cancellationToken
                )
            );
        }
    }

    private static async Task RemoveCategoryReferencesAsync
    (
        SqliteConnection  connection,
        SqliteTransaction transaction,
        long              projectID,
        long              categoryID,
        CancellationToken cancellationToken
    )
    {
        var categories = (await connection.QueryAsync<CharacterCategory>
                          (
                              new CommandDefinition
                              (
                                  "SELECT * FROM character_categories WHERE project_id = @projectID AND id <> @categoryID",
                                  new { projectID, categoryID },
                                  transaction,
                                  cancellationToken: cancellationToken
                              )
                          )).ToList();

        foreach (var category in categories.Where(item => item.ParentCategoryIDs.Contains(categoryID)))
        {
            var parentIDs = category.ParentCategoryIDs.Where(id => id != categoryID).ToArray();
            await connection.ExecuteAsync
            (
                new CommandDefinition
                (
                    "UPDATE character_categories SET parent_category_ids = @parentIDs WHERE id = @id",
                    new { id = category.ID, parentIDs },
                    transaction,
                    cancellationToken: cancellationToken
                )
            );
        }

        var characters = (await connection.QueryAsync<Character>
                          (
                              new CommandDefinition
                              (
                                  "SELECT * FROM characters WHERE project_id = @projectID",
                                  new { projectID },
                                  transaction,
                                  cancellationToken: cancellationToken
                              )
                          )).ToList();

        foreach (var character in characters.Where(item => item.CategoryIDs.Contains(categoryID)))
        {
            var categoryIDs = character.CategoryIDs.Where(id => id != categoryID).ToArray();
            await connection.ExecuteAsync
            (
                new CommandDefinition
                (
                    "UPDATE characters SET category_ids = @categoryIDs WHERE id = @id",
                    new { id = character.ID, categoryIDs },
                    transaction,
                    cancellationToken: cancellationToken
                )
            );
        }

        await connection.ExecuteAsync
        (
            new CommandDefinition
            (
                "DELETE FROM character_category_resolutions WHERE character_id IN (SELECT id FROM characters WHERE project_id = @projectID)",
                new { projectID },
                transaction,
                cancellationToken: cancellationToken
            )
        );
    }

    private static StateAttributeConfig BuildConfig
    (
        StateAttributeDefinition          definition,
        IReadOnlyDictionary<string, long> groupIDs,
        IReadOnlyDictionary<string, long> entryIDs
    )
    {
        var numeric     = definition.Numeric;
        var enumeration = definition.Enumeration;
        var phases = definition.Phases.Select
        (phase => new Phase
            {
                Name              = phase.Name,
                Expression        = phase.Expression,
                KnowledgeIDs      = phase.KnowledgeEntryIDs.Concat(phase.KnowledgeEntryKeys.Select(key => entryIDs[key])).Distinct().ToList(),
                KnowledgeGroupIDs = phase.KnowledgeGroupIDs.Concat(phase.KnowledgeGroupKeys.Select(key => groupIDs[key])).Distinct().ToList(),
                EnterDirectives   = phase.EnterDirectives,
                ExitDirectives    = phase.ExitDirectives
            }
        ).ToList();

        return new StateAttributeConfig
        {
            Min         = numeric?.Min,
            Max         = numeric?.Max,
            Initial     = numeric?.Initial,
            Unit        = numeric?.Unit,
            ChangeRules = numeric?.ChangeRules,
            NumericChanges = numeric?.Changes ?? [],
            Options     = enumeration?.Options,
            Trigger     = enumeration?.Trigger.ToString(),
            Transitions = enumeration?.Transitions,
            Phases      = phases
        };
    }

    private static ProjectStateAttribute ToProjectStateAttribute(StateAttribute attribute)
    {
        var config = ParseConfig(attribute.Config);

        return new ProjectStateAttribute
        (
            attribute.ID,
            attribute.ProjectID,
            attribute.Name,
            attribute.DisplayName,
            attribute.Scope,
            attribute.CategoryID,
            attribute.ValueType,
            attribute.Driver,
            config
        );
    }

    private static StateAttributeConfig ParseConfig(string json) =>
        string.IsNullOrWhiteSpace(json) ?
            new StateAttributeConfig() :
            JsonSerializer.Deserialize<StateAttributeConfig>(json, JsonOptions.Compact) ?? new StateAttributeConfig();

    private static (int Transitions, int PhaseReferences) CountDependencies
    (
        ProjectContentSnapshot snapshot,
        IReadOnlySet<string>   attributeNames
    )
    {
        var transitions     = 0;
        var phaseReferences = 0;

        foreach (var attribute in snapshot.StateAttributes)
        {
            transitions     += attribute.Configuration.Transitions?.Count(transition => attributeNames.Contains(transition.AttributeName ?? string.Empty)) ?? 0;
            phaseReferences += attribute.Configuration.Phases.Sum(phase => phase.KnowledgeIDs.Count + phase.KnowledgeGroupIDs.Count);
        }

        return (transitions, phaseReferences);
    }

    private async Task EnsureProjectExistsAsync(long projectID, CancellationToken cancellationToken)
    {
        if (await projectRepository.GetByIDAsync(projectID, cancellationToken) is null)
            throw new InvalidOperationException($"项目不存在: ID={projectID}");
    }

    private Task EnsureEntityProjectAsync(string table, long id, long projectID, CancellationToken cancellationToken) =>
        scheduler.ExecuteReadAsync
        (
            async (connection, token) =>
            {
                var found = await connection.ExecuteScalarAsync<long?>
                            (
                                new CommandDefinition
                                (
                                    $"SELECT id FROM \"{table}\" WHERE id = @id AND project_id = @projectID",
                                    new { id, projectID },
                                    cancellationToken: token
                                )
                            );

                if (found is null)
                    throw new InvalidOperationException($"记录不存在或不属于项目: {table}/{id}");
            },
            cancellationToken: cancellationToken
        );

    private async Task ValidateKnowledgeGroupAsync(long projectID, long? groupID, CancellationToken cancellationToken)
    {
        if (groupID is null)
            return;

        await EnsureEntityProjectAsync("knowledge_groups", groupID.Value, projectID, cancellationToken);
    }

    private async Task ValidateCategoryParentsAsync
    (
        long                projectID,
        IReadOnlyList<long> parentIDs,
        long?               categoryID,
        CancellationToken   cancellationToken
    )
    {
        if (categoryID is not null && parentIDs.Contains(categoryID.Value))
            throw new ArgumentException("人物分类不能将自身设为父分类", nameof(parentIDs));

        foreach (var parentID in parentIDs.Distinct())
            await EnsureEntityProjectAsync("character_categories", parentID, projectID, cancellationToken);
    }

    private async Task ValidateStateDefinitionAsync
    (
        long                     projectID,
        StateAttributeDefinition definition,
        long?                    attributeID,
        CancellationToken        cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(definition.Name) || string.IsNullOrWhiteSpace(definition.DisplayName))
            throw new ArgumentException("状态属性名称和显示名称不能为空", nameof(definition));

        if (definition.Scope == StateScope.Category)
        {
            if (definition.CategoryID is null)
                throw new ArgumentException("分类状态属性必须指定分类 ID", nameof(definition));

            await EnsureEntityProjectAsync("character_categories", definition.CategoryID.Value, projectID, cancellationToken);
        }

        if (definition.ValueType == StateValueType.Numeric && definition.Numeric is { } numeric)
        {
            if (numeric.Min is not null && numeric.Max is not null && numeric.Min > numeric.Max)
                throw new ArgumentException("状态属性最小值不能大于最大值", nameof(definition));

            if (numeric.Initial is not null &&
                ((numeric.Min is not null && numeric.Initial < numeric.Min) ||
                 (numeric.Max is not null && numeric.Initial > numeric.Max)))
                throw new ArgumentException("状态属性初始值必须位于最小值和最大值之间", nameof(definition));

            if (numeric.Changes.Any(change => string.IsNullOrWhiteSpace(change.Expression) || string.IsNullOrWhiteSpace(change.ChangeExpression)))
                throw new ArgumentException("数值变更条件必须指定条件和数值变更式", nameof(definition));
        }

        foreach (var phase in definition.Phases)
        {
            foreach (var entryID in phase.KnowledgeEntryIDs)
                await EnsureEntityProjectAsync("knowledge_entries", entryID, projectID, cancellationToken);

            foreach (var groupID in phase.KnowledgeGroupIDs)
                await EnsureEntityProjectAsync("knowledge_groups", groupID, projectID, cancellationToken);
        }
    }

    private static void ValidateProjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("项目名称不能为空", nameof(name));
    }

    private static void ValidateBlueprint(ProjectBlueprint blueprint)
    {
        ValidateKeys(blueprint.KnowledgeGroups.Select(group => group.Key),                                    "知识分组");
        ValidateKeys(blueprint.CharacterCategories.Select(category => category.Key),                          "人物分类");
        ValidateKeys(blueprint.KnowledgeGroups.SelectMany(group => group.Entries).Select(entry => entry.Key), "知识条目");

        var categoryKeys = blueprint.CharacterCategories.Select(category => category.Key).ToHashSet(StringComparer.Ordinal);
        var groupKeys    = blueprint.KnowledgeGroups.Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var entryKeys    = blueprint.KnowledgeGroups.SelectMany(group => group.Entries).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var category in blueprint.CharacterCategories)
        {
            if (category.ParentCategoryKeys.Any(key => !categoryKeys.Contains(key) || key == category.Key))
                throw new ArgumentException("人物分类父引用无效", nameof(blueprint));
        }

        foreach (var definition in blueprint.StateAttributes.Concat(blueprint.CharacterCategories.SelectMany(category => category.StateAttributes)))
        {
            foreach (var phase in definition.Phases)
            {
                if (phase.KnowledgeGroupKeys.Any(key => !groupKeys.Contains(key)) ||
                    phase.KnowledgeEntryKeys.Any(key => !entryKeys.Contains(key)))
                    throw new ArgumentException("阶段知识引用无效", nameof(blueprint));
            }
        }
    }

    private static void ValidateKeys(IEnumerable<string> keys, string entityName)
    {
        var values = keys.ToList();

        if (values.Any(string.IsNullOrWhiteSpace) || values.Distinct(StringComparer.Ordinal).Count() != values.Count)
            throw new ArgumentException($"{entityName} key 必须非空且唯一");
    }

    private void NotifyChanged(long projectID, bool isDeleted)
    {
        ProjectContentChange? change = null;

        lock (changeSync)
        {
            if (changeBatchDepth > 0)
            {
                pendingChanges[projectID] = isDeleted ||
                                            pendingChanges.GetValueOrDefault(projectID);
                return;
            }

            change = new ProjectContentChange(projectID, isDeleted);
        }

        Changed?.Invoke(change);
    }

    private void CompleteChangeBatch()
    {
        ProjectContentChange[] changes;

        lock (changeSync)
        {
            if (changeBatchDepth == 0)
                throw new InvalidOperationException("项目变更批次尚未开始");

            changeBatchDepth--;

            if (changeBatchDepth > 0)
                return;

            changes = [.. pendingChanges.Select(change => new ProjectContentChange(change.Key, change.Value))];
            pendingChanges.Clear();
        }

        foreach (var change in changes)
            Changed?.Invoke(change);
    }

    private sealed class ChangeBatch
    (
        ProjectContentService service
    ) : IDisposable
    {
        private ProjectContentService? service = service;

        public void Dispose()
        {
            var value = Interlocked.Exchange(ref service, null);
            value?.CompleteChangeBatch();
        }
    }
}
