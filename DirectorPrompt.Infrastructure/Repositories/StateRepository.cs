using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class StateRepository
(
    SQLiteConnectionFactory connectionFactory
) : IStateRepository
{
    public async Task<StateAttribute?> GetAttributeAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<StateAttribute>
               (
                   "SELECT * FROM state_attributes WHERE id = @id",
                   new { id }
               );
    }

    public async Task<IReadOnlyList<StateAttribute>> GetAttributesAsync
    (
        long              projectID,
        StateScope?       scope             = null,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        IEnumerable<StateAttribute> rows;

        if (scope.HasValue)
        {
            rows = await connection.QueryAsync<StateAttribute>
                   (
                       "SELECT * FROM state_attributes WHERE project_id = @projectID AND scope = @scope",
                       new { projectID, scope = scope.Value }
                   );
        }
        else
        {
            rows = await connection.QueryAsync<StateAttribute>
                   (
                       "SELECT * FROM state_attributes WHERE project_id = @projectID",
                       new { projectID }
                   );
        }

        return rows.ToList();
    }

    public async Task<IReadOnlyList<StateAttribute>> GetAttributesByCategoryAsync
    (
        long              categoryID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<StateAttribute>
                   (
                       "SELECT * FROM state_attributes WHERE category_id = @categoryID",
                       new { categoryID }
                   );

        return rows.ToList();
    }

    public async Task<StateAttribute> CreateAttributeAsync(StateAttribute attribute, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

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
                         scope       = attribute.Scope,
                         categoryID  = attribute.CategoryID,
                         valueType   = attribute.ValueType,
                         driver      = attribute.Driver,
                         config      = attribute.Config
                     }
                 );

        return attribute with { ID = id };
    }

    public async Task UpdateAttributeAsync(StateAttribute attribute, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

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
                scope       = attribute.Scope,
                categoryID  = attribute.CategoryID,
                valueType   = attribute.ValueType,
                driver      = attribute.Driver,
                config      = attribute.Config
            }
        );
    }

    public async Task DeleteAttributeAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        await connection.ExecuteAsync("DELETE FROM state_values WHERE attribute_id = @id",      new { id });
        await connection.ExecuteAsync("DELETE FROM state_change_logs WHERE attribute_id = @id", new { id });
        await connection.ExecuteAsync("DELETE FROM state_attributes WHERE id = @id",            new { id });
    }

    public async Task<StateValue?> GetStateValueAsync(long attributeID, long sessionID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        return await connection.QueryFirstOrDefaultAsync<StateValue>
               (
                   "SELECT * FROM state_values WHERE attribute_id = @attributeID AND session_id = @sessionID",
                   new { attributeID, sessionID }
               );
    }

    public async Task<IReadOnlyList<StateValue>> GetStateValuesAsync
    (
        IReadOnlyList<long> attributeIDs,
        long                sessionID,
        CancellationToken   cancellationToken = default
    )
    {
        if (attributeIDs.Count == 0)
            return [];

        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<StateValue>
                   (
                       "SELECT * FROM state_values WHERE attribute_id IN @attributeIDs AND session_id = @sessionID",
                       new { attributeIDs, sessionID }
                   );

        return rows.ToList();
    }

    public async Task<IReadOnlyList<StateValue>> GetAllStateValuesAsync
    (
        long              projectID,
        long              sessionID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<StateValue>
                   (
                       """
                       SELECT sv.* FROM state_values sv
                       JOIN state_attributes sa ON sa.id = sv.attribute_id
                       WHERE sa.project_id = @projectID AND sv.session_id = @sessionID
                       """,
                       new { projectID, sessionID }
                   );

        return rows.ToList();
    }

    public async Task SetStateValueAsync
    (
        long              attributeID,
        long              sessionID,
        string            value,
        StateChangeSource source,
        string            reason,
        long              sceneID,
        long?             roundID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection  = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var oldValue = await connection.QueryFirstOrDefaultAsync<string>
                           (
                               "SELECT value FROM state_values WHERE attribute_id = @attributeID AND session_id = @sessionID",
                               new { attributeID, sessionID },
                               transaction
                           ) ??
                           string.Empty;

            await connection.ExecuteAsync
            (
                """
                INSERT INTO state_values (attribute_id, session_id, value, updated_at)
                VALUES (@attributeID, @sessionID, @value, @updatedAt)
                ON CONFLICT(attribute_id, session_id)
                DO UPDATE SET value = @value, updated_at = @updatedAt
                """,
                new
                {
                    attributeID,
                    sessionID,
                    value,
                    updatedAt = DateTime.UtcNow
                },
                transaction
            );

            await connection.ExecuteAsync
            (
                """
                INSERT INTO state_change_logs (attribute_id, session_id, scene_id, round_id, old_value, new_value, source, reason, created_at)
                VALUES (@attributeID, @sessionID, @sceneID, @roundID, @oldValue, @newValue, @source, @reason, @createdAt)
                """,
                new
                {
                    attributeID,
                    sessionID,
                    sceneID,
                    roundID,
                    oldValue,
                    newValue = value,
                    source,
                    reason,
                    createdAt = DateTime.UtcNow
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

    public async Task<IReadOnlyList<StateChangeLog>> GetChangeLogsAsync
    (
        long              attributeID,
        long?             sceneID           = null,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);

        IEnumerable<StateChangeLog> rows;

        if (sceneID.HasValue)
        {
            rows = await connection.QueryAsync<StateChangeLog>
                   (
                       "SELECT * FROM state_change_logs WHERE attribute_id = @attributeID AND scene_id = @sceneID ORDER BY created_at DESC",
                       new { attributeID, sceneID = sceneID.Value }
                   );
        }
        else
        {
            rows = await connection.QueryAsync<StateChangeLog>
                   (
                       "SELECT * FROM state_change_logs WHERE attribute_id = @attributeID ORDER BY created_at DESC",
                       new { attributeID }
                   );
        }

        return rows.ToList();
    }

    public async Task RollbackByRoundAsync
    (
        long              sessionID,
        long              roundID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection  = await connectionFactory.CreateAsync(cancellationToken: cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var logs = await connection.QueryAsync
                       (
                           """
                           SELECT attribute_id, session_id, old_value
                           FROM state_change_logs
                           WHERE round_id = @roundID AND session_id = @sessionID
                           ORDER BY id ASC
                           """,
                           new { roundID, sessionID },
                           transaction
                       );

            var firstPerAttribute = new Dictionary<long, string>();

            foreach (var log in logs)
            {
                var attrID = (long)log.attribute_id;

                if (!firstPerAttribute.ContainsKey(attrID))
                    firstPerAttribute[attrID] = (string)log.old_value;
            }

            foreach (var (attrID, oldValue) in firstPerAttribute)
            {
                await connection.ExecuteAsync
                (
                    """
                    UPDATE state_values
                    SET value = @oldValue, updated_at = @updatedAt
                    WHERE attribute_id = @attrID AND session_id = @sessionID
                    """,
                    new
                    {
                        attrID,
                        sessionID,
                        oldValue,
                        updatedAt = DateTime.UtcNow
                    },
                    transaction
                );
            }

            await connection.ExecuteAsync
            (
                "DELETE FROM state_change_logs WHERE round_id = @roundID AND session_id = @sessionID",
                new { roundID, sessionID },
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
}
