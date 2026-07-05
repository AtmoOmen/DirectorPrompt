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

    public async Task<Project> CreateAsync(Project project, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var now = DateTime.UtcNow.ToString("O");

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO projects (name, world_overview, narrative_style, permanent_constraints, created_at, updated_at)
                     VALUES (@name, @worldOverview, @narrativeStyle, @permanentConstraints, @createdAt, @updatedAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         name                 = project.Name,
                         worldOverview        = project.WorldOverview,
                         narrativeStyle       = project.NarrativeStyle,
                         permanentConstraints = JsonHelper.Serialize(project.PermanentConstraints),
                         createdAt            = now,
                         updatedAt            = now
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
                world_overview = @worldOverview,
                narrative_style = @narrativeStyle,
                permanent_constraints = @permanentConstraints,
                updated_at = @updatedAt
            WHERE id = @id
            """,
            new
            {
                id                   = project.ID,
                name                 = project.Name,
                worldOverview        = project.WorldOverview,
                narrativeStyle       = project.NarrativeStyle,
                permanentConstraints = JsonHelper.Serialize(project.PermanentConstraints),
                updatedAt            = DateTime.UtcNow.ToString("O")
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
        public long   ID                    { get; set; }
        public string Name                  { get; set; } = string.Empty;
        public string World_Overview        { get; set; } = string.Empty;
        public string Narrative_Style       { get; set; } = string.Empty;
        public string Permanent_Constraints { get; set; } = "[]";
        public string Created_At            { get; set; } = string.Empty;
        public string Updated_At            { get; set; } = string.Empty;

        public Project ToProject() =>
            new()
            {
                ID                   = ID,
                Name                 = Name,
                WorldOverview        = World_Overview,
                NarrativeStyle       = Narrative_Style,
                PermanentConstraints = JsonHelper.DeserializeStringArray(Permanent_Constraints),
                CreatedAt            = DateTime.Parse(Created_At),
                UpdatedAt            = DateTime.Parse(Updated_At)
            };
    }
}
