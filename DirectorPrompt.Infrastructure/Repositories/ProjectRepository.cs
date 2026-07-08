using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

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
                    INSERT INTO projects (name, description, opening_message, audit_config, memory_config, knowledge_config, created_at, updated_at)
                    VALUES (@name, @description, @openingMessage, @auditConfig, @memoryConfig, @knowledgeConfig, @createdAt, @updatedAt);
                    SELECT last_insert_rowid();
                    """,
                    new
                    {
                        name            = project.Name,
                        description     = project.Description,
                        openingMessage  = project.OpeningMessage,
                        auditConfig     = project.AuditConfig,
                        memoryConfig    = project.MemoryConfig,
                        knowledgeConfig = project.KnowledgeConfig,
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
                audit_config = @auditConfig,
                memory_config = @memoryConfig,
                knowledge_config = @knowledgeConfig,
                updated_at = @updatedAt
            WHERE id = @id
            """,
            new
            {
                id              = project.ID,
                name            = project.Name,
                description     = project.Description,
                openingMessage  = project.OpeningMessage,
                auditConfig     = project.AuditConfig,
                memoryConfig    = project.MemoryConfig,
                knowledgeConfig = project.KnowledgeConfig,
                updatedAt       = DateTime.UtcNow.ToString("O")
            }
        );
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync("DELETE FROM projects WHERE id = @id", new { id });
    }

    private sealed class ProjectRow
    {
        public long ID { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Opening_Message { get; set; } = string.Empty;

        public string Audit_Config { get; set; } = "{}";

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
                AuditConfig     = Audit_Config,
                MemoryConfig    = Memory_Config,
                KnowledgeConfig = Knowledge_Config,
                CreatedAt       = DateTime.Parse(Created_At),
                UpdatedAt       = DateTime.Parse(Updated_At)
            };
    }
}
