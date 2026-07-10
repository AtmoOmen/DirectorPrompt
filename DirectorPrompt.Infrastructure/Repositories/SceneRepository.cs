using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class SceneRepository : ISceneRepository
{
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly IRoundChangeRepository  roundChangeRepository;

    public SceneRepository(SqliteConnectionFactory connectionFactory, IRoundChangeRepository roundChangeRepository)
    {
        this.connectionFactory     = connectionFactory;
        this.roundChangeRepository = roundChangeRepository;
    }

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

    public async Task<IReadOnlyList<Scene>> GetBySessionAsync(long sessionID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<SceneRow>
                   (
                       "SELECT * FROM scenes WHERE session_id = @sessionID ORDER BY id",
                       new { sessionID }
                   );

        return rows.Select(r => r.ToScene()).ToList();
    }

    public async Task<Scene?> GetActiveSceneAsync(long sessionID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<SceneRow>
                  (
                      "SELECT * FROM scenes WHERE session_id = @sessionID AND status = 'active' ORDER BY id DESC LIMIT 1",
                      new { sessionID }
                  );

        return row?.ToScene();
    }

    public async Task<IReadOnlyList<Scene>> GetOrderedByTimelineAsync(long sessionID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<SceneRow>
                   (
                       "SELECT * FROM scenes WHERE session_id = @sessionID ORDER BY timeline_position",
                       new { sessionID }
                   );

        return rows.Select(r => r.ToScene()).ToList();
    }

    public async Task<Scene> CreateAsync(Scene scene, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO scenes (project_id, session_id, timeline_position, time_label, summary, progress_summary, status)
                     VALUES (@projectID, @sessionID, @timelinePosition, @timeLabel, @summary, @progressSummary, @status);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID        = scene.ProjectID,
                         sessionID        = scene.SessionID,
                         timelinePosition = scene.TimelinePosition,
                         timeLabel        = scene.TimeLabel,
                         summary          = scene.Summary,
                         progressSummary  = scene.ProgressSummary,
                         status           = scene.Status.ToString().ToLowerInvariant()
                     }
                 );

        await roundChangeRepository.RecordCreateAsync(RoundContext.Current ?? 0, "scenes", id, cancellationToken: cancellationToken);

        return scene with { ID = id };
    }

    public async Task UpdateAsync(Scene scene, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var oldRow = await connection.QueryFirstOrDefaultAsync<IDictionary<string, object>>
                     (
                         "SELECT * FROM scenes WHERE id = @id",
                         new { id = scene.ID }
                     );

        await connection.ExecuteAsync
        (
            """
            UPDATE scenes
            SET time_label = @timeLabel,
                summary = @summary,
                progress_summary = @progressSummary,
                status = @status
            WHERE id = @id
            """,
            new
            {
                id              = scene.ID,
                timeLabel       = scene.TimeLabel,
                summary         = scene.Summary,
                progressSummary = scene.ProgressSummary,
                status          = scene.Status.ToString().ToLowerInvariant()
            }
        );

        if (oldRow is not null)
            await roundChangeRepository.RecordUpdateAsync(RoundContext.Current ?? 0, "scenes", scene.ID, JsonSerializer.Serialize(oldRow), cancellationToken);
    }

    public async Task CloseActiveSceneAsync(long sessionID, string? summary, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var oldRow = await connection.QueryFirstOrDefaultAsync<IDictionary<string, object>>
                     (
                         "SELECT * FROM scenes WHERE session_id = @sessionID AND status = 'active' ORDER BY id DESC LIMIT 1",
                         new { sessionID }
                     );

        if (oldRow is null)
            return;

        var sceneID = (long)oldRow["id"];

        await connection.ExecuteAsync
        (
            "UPDATE scenes SET status = 'completed', summary = @summary WHERE id = @id",
            new { id = sceneID, summary }
        );

        await roundChangeRepository.RecordUpdateAsync(RoundContext.Current ?? 0, "scenes", sceneID, JsonSerializer.Serialize(oldRow), cancellationToken);
    }

    public async Task<Scene?> GetLastCompletedSceneAsync(long sessionID, long beforeSceneID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<SceneRow>
                  (
                      "SELECT * FROM scenes WHERE session_id = @sessionID AND status = 'completed' AND id < @beforeSceneID AND summary IS NOT NULL ORDER BY id DESC LIMIT 1",
                      new { sessionID, beforeSceneID }
                  );

        return row?.ToScene();
    }

    private sealed class SceneRow
    {
        public long    ID                { get; set; }
        public long    Project_ID        { get; set; }
        public long?   Session_ID        { get; set; }
        public long    Timeline_Position { get; set; }
        public string  Time_Label        { get; set; } = string.Empty;
        public string? Summary           { get; set; }
        public string? Progress_Summary  { get; set; }
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
                SessionID        = Session_ID ?? 0,
                TimelinePosition = Timeline_Position,
                TimeLabel        = Time_Label,
                Summary          = Summary,
                ProgressSummary  = Progress_Summary,
                Status           = status
            };
        }
    }
}
