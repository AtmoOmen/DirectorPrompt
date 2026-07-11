using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using Microsoft.Data.Sqlite;
using Serilog;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public ProjectRepository(SqliteConnectionFactory connectionFactory) =>
        this.connectionFactory = connectionFactory;

    public async Task<Project?> GetByIDAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<ProjectRow>
                  (
                      "SELECT * FROM projects WHERE id = @id",
                      new { id }
                  );

        return row?.ToProject();
    }

    public async Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<ProjectRow>
                   (
                       "SELECT * FROM projects ORDER BY updated_at DESC"
                   );

        return rows.Select(r => r.ToProject()).ToList();
    }

    public async Task<Project> CreateAsync(Project project, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var now = DateTime.UtcNow.ToString("O");

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO projects (name, description, opening_message, created_at, updated_at)
                     VALUES (@name, @description, @openingMessage, @createdAt, @updatedAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         name            = project.Name,
                         description     = project.Description,
                         openingMessage  = project.OpeningMessage,
                         createdAt       = now,
                         updatedAt       = now
                     }
                 );

        return project with { ID = id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
    }

    public async Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
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
                id              = project.ID,
                name            = project.Name,
                description     = project.Description,
                openingMessage  = project.OpeningMessage,
                updatedAt       = DateTime.UtcNow.ToString("O")
            }
        );
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection  = await connectionFactory.CreateAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync
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
                transaction
            );

            await DropVectorTableAsync(connection, transaction, VectorTableManager.GetKnowledgeTableName(id), cancellationToken);
            await DropVectorTableAsync(connection, transaction, VectorTableManager.GetMemoryTableName(id),    cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

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

    private sealed class ProjectRow
    {
        public long ID { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Opening_Message { get; set; } = string.Empty;

        public string Created_At { get; set; } = string.Empty;

        public string Updated_At { get; set; } = string.Empty;

        public Project ToProject() =>
            new()
            {
                ID              = ID,
                Name            = Name,
                Description     = Description,
                OpeningMessage  = Opening_Message,
                CreatedAt       = DateTime.Parse(Created_At),
                UpdatedAt       = DateTime.Parse(Updated_At)
            };
    }
}
