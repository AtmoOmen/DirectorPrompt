using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class StateRepository : IStateRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public StateRepository(SqliteConnectionFactory connectionFactory) =>
        this.connectionFactory = connectionFactory;

    public async Task<StateAttribute?> GetAttributeAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<StateAttributeRow>
                  (
                      "SELECT * FROM state_attributes WHERE id = @id",
                      new { id }
                  );

        return row?.ToStateAttribute();
    }

    public async Task<IReadOnlyList<StateAttribute>> GetAttributesAsync
    (
        long              projectID,
        StateScope?       scope             = null,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        IEnumerable<StateAttributeRow> rows;

        if (scope.HasValue)
        {
            rows = await connection.QueryAsync<StateAttributeRow>
                   (
                       "SELECT * FROM state_attributes WHERE project_id = @projectID AND scope = @scope",
                       new { projectID, scope = scope.Value.ToString().ToLowerInvariant() }
                   );
        }
        else
        {
            rows = await connection.QueryAsync<StateAttributeRow>
                   (
                       "SELECT * FROM state_attributes WHERE project_id = @projectID",
                       new { projectID }
                   );
        }

        return rows.Select(r => r.ToStateAttribute()).ToList();
    }

    public async Task<IReadOnlyList<StateAttribute>> GetAttributesByCategoryAsync
    (
        long              categoryID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<StateAttributeRow>
                   (
                       "SELECT * FROM state_attributes WHERE category_id = @categoryID",
                       new { categoryID }
                   );

        return rows.Select(r => r.ToStateAttribute()).ToList();
    }

    public async Task<StateAttribute> CreateAttributeAsync(StateAttribute attribute, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO state_attributes (project_id, name, display_name, scope, category_id, value_type, driver, config)
                     VALUES (@projectID, @name, @displayName, @scope, @categoryID, @valueType, @driver, @config);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID   = attribute.ProjectID,
                         name        = attribute.Name,
                         displayName = attribute.DisplayName,
                         scope       = attribute.Scope.ToString().ToLowerInvariant(),
                         categoryID  = attribute.CategoryID,
                         valueType   = attribute.ValueType.ToString().ToLowerInvariant(),
                         driver      = attribute.Driver.ToString().ToLowerInvariant(),
                         config      = attribute.Config
                     }
                 );

        return attribute with { ID = id };
    }

    public async Task UpdateAttributeAsync(StateAttribute attribute, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
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
            WHERE id = @id
            """,
            new
            {
                id          = attribute.ID,
                name        = attribute.Name,
                displayName = attribute.DisplayName,
                scope       = attribute.Scope.ToString().ToLowerInvariant(),
                categoryID  = attribute.CategoryID,
                valueType   = attribute.ValueType.ToString().ToLowerInvariant(),
                driver      = attribute.Driver.ToString().ToLowerInvariant(),
                config      = attribute.Config
            }
        );
    }

    public async Task<StateValue?> GetStateValueAsync(long attributeID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<StateValueRow>
                  (
                      "SELECT * FROM state_values WHERE attribute_id = @attributeID",
                      new { attributeID }
                  );

        if (row is null)
            return null;

        return new StateValue
        {
            AttributeID = row.Attribute_ID,
            Value       = row.Value,
            UpdatedAt   = DateTime.Parse(row.Updated_At)
        };
    }

    public async Task<IReadOnlyList<StateValue>> GetAllStateValuesAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<StateValueRow>
                   (
                       """
                       SELECT sv.* FROM state_values sv
                       JOIN state_attributes sa ON sa.id = sv.attribute_id
                       WHERE sa.project_id = @projectID
                       """,
                       new { projectID }
                   );

        return rows.Select
        (r => new StateValue
            {
                AttributeID = r.Attribute_ID,
                Value       = r.Value,
                UpdatedAt   = DateTime.Parse(r.Updated_At)
            }
        ).ToList();
    }

    public async Task SetStateValueAsync
    (
        long              attributeID,
        string            value,
        StateChangeSource source,
        string            reason,
        long              sceneID,
        long?             roundID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection  = await connectionFactory.CreateAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var oldValue = await connection.QueryFirstOrDefaultAsync<string>
                           (
                               "SELECT value FROM state_values WHERE attribute_id = @attributeID",
                               new { attributeID },
                               transaction
                           ) ??
                           string.Empty;

            await connection.ExecuteAsync
            (
                """
                INSERT INTO state_values (attribute_id, value, updated_at)
                VALUES (@attributeID, @value, @updatedAt)
                ON CONFLICT(attribute_id)
                DO UPDATE SET value = @value, updated_at = @updatedAt
                """,
                new
                {
                    attributeID,
                    value,
                    updatedAt = DateTime.UtcNow.ToString("O")
                },
                transaction
            );

            await connection.ExecuteAsync
            (
                """
                INSERT INTO state_change_logs (attribute_id, scene_id, round_id, old_value, new_value, source, reason, created_at)
                VALUES (@attributeID, @sceneID, @roundID, @oldValue, @newValue, @source, @reason, @createdAt)
                """,
                new
                {
                    attributeID,
                    sceneID,
                    roundID,
                    oldValue,
                    newValue = value,
                    source   = source.ToString().ToLowerInvariant(),
                    reason,
                    createdAt = DateTime.UtcNow.ToString("O")
                },
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

    public async Task<IReadOnlyList<CompositeItem>> GetCompositeItemsAsync(long attributeID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<CompositeItemRow>
                   (
                       "SELECT * FROM composite_items WHERE attribute_id = @attributeID",
                       new { attributeID }
                   );

        return rows.Select(r => r.ToCompositeItem()).ToList();
    }

    public async Task<CompositeItem> AddCompositeItemAsync(CompositeItem item, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO composite_items (attribute_id, description, current, target, status)
                     VALUES (@attributeID, @description, @current, @target, @status);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         attributeID = item.AttributeID,
                         description = item.Description,
                         current     = item.Current,
                         target      = item.Target,
                         status      = item.Status.ToString().ToLowerInvariant()
                     }
                 );

        return item with { ID = id };
    }

    public async Task<CompositeItem> UpdateCompositeItemAsync
    (
        long              itemID,
        float?            delta,
        float?            current,
        string            reason,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QuerySingleAsync<CompositeItemRow>
                  (
                      "SELECT * FROM composite_items WHERE id = @itemID",
                      new { itemID }
                  );

        var newCurrent = current ??
                         (delta.HasValue ?
                              row.Current + delta.Value :
                              row.Current);

        var newStatus = newCurrent >= row.Target ?
                            "completed" :
                            row.Status;

        await connection.ExecuteAsync
        (
            """
            UPDATE composite_items
            SET current = @current, status = @status
            WHERE id = @id
            """,
            new { id = itemID, current = newCurrent, status = newStatus }
        );

        return new CompositeItem
        {
            ID          = itemID,
            AttributeID = row.Attribute_ID,
            Description = row.Description,
            Current     = newCurrent,
            Target      = row.Target,
            Status = newStatus switch
            {
                "completed" => CompositeItemStatus.Completed,
                "failed"    => CompositeItemStatus.Failed,
                _           => CompositeItemStatus.Active
            }
        };
    }

    public async Task RemoveCompositeItemAsync(long itemID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync("DELETE FROM composite_items WHERE id = @itemID", new { itemID });
    }

    public async Task<IReadOnlyList<Flag>> GetFlagsAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<FlagRow>
                   (
                       "SELECT * FROM flags WHERE project_id = @projectID",
                       new { projectID }
                   );

        return rows.Select(r => r.ToFlag()).ToList();
    }

    public async Task SetFlagAsync(long projectID, string name, bool value, long? sceneID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            """
            INSERT INTO flags (project_id, name, display_name, value, set_at_scene_id)
            VALUES (@projectID, @name, @name, @value, @sceneID)
            ON CONFLICT(project_id, name)
            DO UPDATE SET value = @value, set_at_scene_id = @sceneID
            """,
            new
            {
                projectID,
                name,
                value = value ?
                            1 :
                            0,
                sceneID
            }
        );
    }

    public async Task<IReadOnlyList<StateChangeLog>> GetChangeLogsAsync
    (
        long              attributeID,
        long?             sceneID           = null,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        IEnumerable<StateChangeLogRow> rows;

        if (sceneID.HasValue)
        {
            rows = await connection.QueryAsync<StateChangeLogRow>
                   (
                       "SELECT * FROM state_change_logs WHERE attribute_id = @attributeID AND scene_id = @sceneID ORDER BY created_at DESC",
                       new { attributeID, sceneID = sceneID.Value }
                   );
        }
        else
        {
            rows = await connection.QueryAsync<StateChangeLogRow>
                   (
                       "SELECT * FROM state_change_logs WHERE attribute_id = @attributeID ORDER BY created_at DESC",
                       new { attributeID }
                   );
        }

        return rows.Select(r => r.ToStateChangeLog()).ToList();
    }

    private sealed class StateAttributeRow
    {
        public long   ID           { get; set; }
        public long   Project_ID   { get; set; }
        public string Name         { get; set; } = string.Empty;
        public string Display_Name { get; set; } = string.Empty;
        public string Scope        { get; set; } = "global";
        public long?  Category_ID  { get; set; }
        public string Value_Type   { get; set; } = "numeric";
        public string Driver       { get; set; } = "narrative";
        public string Config       { get; set; } = "{}";

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

    private sealed class StateValueRow
    {
        public long   Attribute_ID { get; set; }
        public string Value        { get; set; } = string.Empty;
        public string Updated_At   { get; set; } = string.Empty;
    }

    private sealed class CompositeItemRow
    {
        public long   ID           { get; set; }
        public long   Attribute_ID { get; set; }
        public string Description  { get; set; } = string.Empty;
        public float  Current      { get; set; }
        public float  Target       { get; set; }
        public string Status       { get; set; } = "active";

        public CompositeItem ToCompositeItem() =>
            new()
            {
                ID          = ID,
                AttributeID = Attribute_ID,
                Description = Description,
                Current     = Current,
                Target      = Target,
                Status = Status switch
                {
                    "completed" => CompositeItemStatus.Completed,
                    "failed"    => CompositeItemStatus.Failed,
                    _           => CompositeItemStatus.Active
                }
            };
    }

    private sealed class FlagRow
    {
        public long   ID              { get; set; }
        public long   Project_ID      { get; set; }
        public string Name            { get; set; } = string.Empty;
        public string Display_Name    { get; set; } = string.Empty;
        public int    Value           { get; set; }
        public long?  Set_At_Scene_ID { get; set; }

        public Flag ToFlag() =>
            new()
            {
                ID           = ID,
                ProjectID    = Project_ID,
                Name         = Name,
                DisplayName  = Display_Name,
                Value        = Value != 0,
                SetAtSceneID = Set_At_Scene_ID
            };
    }

    private sealed class StateChangeLogRow
    {
        public long   ID           { get; set; }
        public long   Attribute_ID { get; set; }
        public long   Scene_ID     { get; set; }
        public long?  Round_ID     { get; set; }
        public string Old_Value    { get; set; } = string.Empty;
        public string New_Value    { get; set; } = string.Empty;
        public string Source       { get; set; } = string.Empty;
        public string Reason       { get; set; } = string.Empty;
        public string Created_At   { get; set; } = string.Empty;

        public StateChangeLog ToStateChangeLog() =>
            new()
            {
                ID          = ID,
                AttributeID = Attribute_ID,
                SceneID     = Scene_ID,
                RoundID     = Round_ID,
                OldValue    = Old_Value,
                NewValue    = New_Value,
                Source = Source switch
                {
                    "system"          => StateChangeSource.System,
                    "director_manual" => StateChangeSource.DirectorManual,
                    _                 => StateChangeSource.StateAgent
                },
                Reason    = Reason,
                CreatedAt = DateTime.Parse(Created_At)
            };
    }
}
