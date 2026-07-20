using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Data.Sqlite;
using Serilog;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class ProjectRepository
(
    SQLiteDatabaseScheduler scheduler
) : IProjectRepository
{
    public Task<Project?> GetByIDAsync(long id, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteReadAsync
        (
            async (connection, token) =>
                await connection.QueryFirstOrDefaultAsync<Project>
                (
                    new CommandDefinition
                    (
                        "SELECT * FROM projects WHERE id = @id",
                        new { id },
                        cancellationToken: token
                    )
                ),
            cancellationToken
        );

    public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default) =>
        scheduler.ExecuteReadAsync<IReadOnlyList<Project>>
        (
            async (connection, token) =>
            {
                var rows = await connection.QueryAsync<Project>
                           (
                               new CommandDefinition
                               (
                                   "SELECT * FROM projects ORDER BY updated_at DESC",
                                   cancellationToken: token
                               )
                           );

                return rows.ToList();
            },
            cancellationToken
        );

    public Task<Project> CreateAsync(Project project, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                var now = DateTime.UtcNow;
                var id = await connection.ExecuteScalarAsync<long>
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
                                     name           = project.Name,
                                     description    = project.Description,
                                     openingMessage = project.OpeningMessage,
                                     createdAt      = now,
                                     updatedAt      = now
                                 },
                                 cancellationToken: token
                             )
                         );

                return project with { ID = id, CreatedAt = now, UpdatedAt = now };
            },
            cancellationToken: cancellationToken
        );

    public Task UpdateAsync(Project project, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        """
                        UPDATE projects
                        SET name = @name,
                            description = @description,
                            opening_message = @openingMessage,
                            updated_at = @updatedAt
                        WHERE id = @id
                        """,
                        new
                        {
                            id             = project.ID,
                            name           = project.Name,
                            description    = project.Description,
                            openingMessage = project.OpeningMessage,
                            updatedAt      = DateTime.UtcNow
                        },
                        cancellationToken: token
                    )
                );
            },
            cancellationToken: cancellationToken
        );

    public Task DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        """
                        DELETE FROM round_changes
                        WHERE round_id IN (SELECT id FROM rounds WHERE project_id = @id);

                        DELETE FROM character_relation_logs
                        WHERE relation_id IN (SELECT id FROM character_relations WHERE project_id = @id);

                        DELETE FROM character_state_values
                        WHERE character_id IN (SELECT id FROM characters WHERE project_id = @id)
                           OR attribute_id IN (SELECT id FROM state_attributes WHERE project_id = @id);

                        DELETE FROM character_category_resolutions
                        WHERE character_id IN (SELECT id FROM characters WHERE project_id = @id);

                        DELETE FROM character_scene_presence
                        WHERE character_id IN (SELECT id FROM characters WHERE project_id = @id)
                           OR scene_id IN (SELECT id FROM scenes WHERE project_id = @id);

                        DELETE FROM state_values
                        WHERE attribute_id IN (SELECT id FROM state_attributes WHERE project_id = @id);

                        DELETE FROM state_change_logs
                        WHERE attribute_id IN (SELECT id FROM state_attributes WHERE project_id = @id);

                        DELETE FROM knowledge_entity_index
                        WHERE entry_id IN (SELECT id FROM knowledge_entries WHERE project_id = @id);

                        DELETE FROM character_relations WHERE project_id = @id;
                        DELETE FROM characters WHERE project_id = @id;
                        DELETE FROM memory_entries WHERE project_id = @id;
                        DELETE FROM active_directives WHERE project_id = @id;
                        DELETE FROM playthrough_events WHERE project_id = @id;
                        DELETE FROM state_attributes WHERE project_id = @id;
                        DELETE FROM knowledge_entries WHERE project_id = @id;
                        DELETE FROM knowledge_groups WHERE project_id = @id;
                        DELETE FROM character_categories WHERE project_id = @id;
                        DELETE FROM rounds WHERE project_id = @id;
                        DELETE FROM scenes WHERE project_id = @id;
                        DELETE FROM sessions WHERE project_id = @id;
                        DELETE FROM projects WHERE id = @id;
                        """,
                        new { id },
                        transaction,
                        cancellationToken: token
                    )
                );

                await DropVectorTableAsync(connection, transaction, VectorTableManager.GetKnowledgeTableName(id), token);
                await DropVectorTableAsync(connection, transaction, VectorTableManager.GetMemoryTableName(id),    token);
                await DropVectorTableAsync(connection, transaction, VectorTableManager.GetCharacterTableName(id), token);
                await transaction.CommitAsync(token);
            },
            cancellationToken: cancellationToken
        );

    private static async Task DropVectorTableAsync
    (
        SqliteConnection  connection,
        SqliteTransaction transaction,
        string            tableName,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DROP TABLE IF EXISTS \"{tableName}\"";
            await command.ExecuteNonQueryAsync(cancellationToken);

            await using var metaCommand = connection.CreateCommand();
            metaCommand.Transaction = transaction;
            metaCommand.CommandText = "DELETE FROM vector_tables WHERE table_name = $tableName";
            metaCommand.Parameters.AddWithValue("$tableName", tableName);
            await metaCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            Log.Warning("向量表 {Table} 删除失败, 可能 vec0 扩展未加载", tableName);
        }
    }
}
