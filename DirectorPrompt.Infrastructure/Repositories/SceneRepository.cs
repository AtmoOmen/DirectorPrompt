using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class SceneRepository : ISceneRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SceneRepository(SqliteConnectionFactory connectionFactory) =>
        this.connectionFactory = connectionFactory;

    public async Task<Scene?> GetByIDAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<SceneRow>
                  (
                      "SELECT * FROM scenes WHERE id = @id",
                      new { id }
                  );

        return row?.ToScene();
    }

    public async Task<IReadOnlyList<Scene>> GetByProjectAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<SceneRow>
                   (
                       "SELECT * FROM scenes WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.Select(r => r.ToScene()).ToList();
    }

    public async Task<Scene?> GetActiveSceneAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<SceneRow>
                  (
                      "SELECT * FROM scenes WHERE project_id = @projectID AND status = 'active' ORDER BY id DESC LIMIT 1",
                      new { projectID }
                  );

        return row?.ToScene();
    }

    public async Task<IReadOnlyList<Scene>> GetOrderedByTimelineAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<SceneRow>
                   (
                       "SELECT * FROM scenes WHERE project_id = @projectID ORDER BY timeline_position",
                       new { projectID }
                   );

        return rows.Select(r => r.ToScene()).ToList();
    }

    public async Task<Scene> CreateAsync(Scene scene, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO scenes (project_id, timeline_position, time_label, summary, status)
                     VALUES (@projectID, @timelinePosition, @timeLabel, @summary, @status);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID        = scene.ProjectID,
                         timelinePosition = scene.TimelinePosition,
                         timeLabel        = scene.TimeLabel,
                         summary          = scene.Summary,
                         status           = scene.Status.ToString().ToLowerInvariant()
                     }
                 );

        return scene with { ID = id };
    }

    public async Task UpdateAsync(Scene scene, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            """
            UPDATE scenes
            SET time_label = @timeLabel,
                summary = @summary,
                status = @status
            WHERE id = @id
            """,
            new
            {
                id        = scene.ID,
                timeLabel = scene.TimeLabel,
                summary   = scene.Summary,
                status    = scene.Status.ToString().ToLowerInvariant()
            }
        );
    }

    private sealed class SceneRow
    {
        public long    ID                { get; set; }
        public long    Project_ID        { get; set; }
        public long    Timeline_Position { get; set; }
        public string  Time_Label        { get; set; } = string.Empty;
        public string? Summary           { get; set; }
        public string  Status            { get; set; } = "active";

        public Scene ToScene()
        {
            var status = Status switch
            {
                "active"    => SceneStatus.Active,
                "completed" => SceneStatus.Completed,
                "archived"  => SceneStatus.Archived,
                _           => SceneStatus.Active
            };

            return new Scene
            {
                ID               = ID,
                ProjectID        = Project_ID,
                TimelinePosition = Timeline_Position,
                TimeLabel        = Time_Label,
                Summary          = Summary,
                Status           = status
            };
        }
    }
}
