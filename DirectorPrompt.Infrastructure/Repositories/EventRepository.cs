using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class EventRepository : IEventRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public EventRepository(SqliteConnectionFactory connectionFactory) =>
        this.connectionFactory = connectionFactory;

    public async Task<PlaythroughEvent> AppendAsync(PlaythroughEvent eventItem, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO playthrough_events (project_id, round_id, type, data, created_at)
                     VALUES (@projectID, @roundID, @type, @data, @createdAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID = eventItem.ProjectID,
                         roundID   = eventItem.RoundID,
                         type      = eventItem.Type.ToString().ToLowerInvariant(),
                         data      = eventItem.Data,
                         createdAt = eventItem.CreatedAt.ToString("O")
                     }
                 );

        return eventItem with { ID = id };
    }

    public async Task<IReadOnlyList<PlaythroughEvent>> GetByProjectAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<EventRow>
                   (
                       "SELECT * FROM playthrough_events WHERE project_id = @projectID ORDER BY id",
                       new { projectID }
                   );

        return rows.Select(r => r.ToEvent()).ToList();
    }

    public async Task<IReadOnlyList<PlaythroughEvent>> GetByRoundAsync(long roundID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<EventRow>
                   (
                       "SELECT * FROM playthrough_events WHERE round_id = @roundID ORDER BY id",
                       new { roundID }
                   );

        return rows.Select(r => r.ToEvent()).ToList();
    }

    public async Task<IReadOnlyList<PlaythroughEvent>> GetAfterAsync(long projectID, long eventID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<EventRow>
                   (
                       "SELECT * FROM playthrough_events WHERE project_id = @projectID AND id > @eventID ORDER BY id",
                       new { projectID, eventID }
                   );

        return rows.Select(r => r.ToEvent()).ToList();
    }

    public async Task RemoveByRoundAsync(long roundID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            "DELETE FROM playthrough_events WHERE round_id = @roundID",
            new { roundID }
        );
    }

    public async Task<long> GetLatestRoundIDAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var result = await connection.QueryFirstOrDefaultAsync<long?>
                     (
                         "SELECT MAX(round_id) FROM playthrough_events WHERE project_id = @projectID",
                         new { projectID }
                     );

        return result ?? 0;
    }

    private sealed class EventRow
    {
        public long   ID         { get; set; }
        public long   Project_ID { get; set; }
        public long   Round_ID   { get; set; }
        public string Type       { get; set; } = string.Empty;
        public string Data       { get; set; } = string.Empty;
        public string Created_At { get; set; } = string.Empty;

        public PlaythroughEvent ToEvent()
        {
            var type = Type switch
            {
                "director_input"   => EventType.DirectorInput,
                "narrative_output" => EventType.NarrativeOutput,
                "state_change"     => EventType.StateChange,
                "memory_update"    => EventType.MemoryUpdate,
                "character_update" => EventType.CharacterUpdate,
                "scene_change"     => EventType.SceneChange,
                "directive_change" => EventType.DirectiveChange,
                _                  => EventType.DirectorInput
            };

            return new PlaythroughEvent
            {
                ID        = ID,
                ProjectID = Project_ID,
                RoundID   = Round_ID,
                Type      = type,
                Data      = Data,
                CreatedAt = DateTime.Parse(Created_At)
            };
        }
    }
}
