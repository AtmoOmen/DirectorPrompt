using System.Text.Json;
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
                     INSERT INTO playthrough_events (project_id, session_id, round_id, type, data, created_at)
                     VALUES (@projectID, @sessionID, @roundID, @type, @data, @createdAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID = eventItem.ProjectID,
                         sessionID = eventItem.SessionID,
                         roundID   = eventItem.RoundID,
                         type      = JsonNamingPolicy.SnakeCaseLower.ConvertName(eventItem.Type.ToString()),
                         data      = eventItem.Data,
                         createdAt = eventItem.CreatedAt.ToString("O")
                     }
                 );

        return eventItem with { ID = id };
    }

    public async Task<IReadOnlyList<PlaythroughEvent>> GetBySessionAsync(long sessionID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<EventRow>
                   (
                       "SELECT * FROM playthrough_events WHERE session_id = @sessionID ORDER BY id",
                       new { sessionID }
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

    public async Task RemoveByRoundAsync(long roundID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            "DELETE FROM playthrough_events WHERE round_id = @roundID",
            new { roundID }
        );
    }

    public async Task<long> GetLatestRoundIDAsync(long sessionID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var result = await connection.QueryFirstOrDefaultAsync<long?>
                     (
                         "SELECT MAX(round_id) FROM playthrough_events WHERE session_id = @sessionID",
                         new { sessionID }
                     );

        return result ?? 0;
    }

    public async Task UpdateEventDataAsync(long eventID, string data, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            "UPDATE playthrough_events SET data = @data WHERE id = @eventID",
            new { eventID, data }
        );
    }

    private sealed class EventRow
    {
        public long   ID         { get; set; }
        public long   Project_ID { get; set; }
        public long?  Session_ID { get; set; }
        public long   Round_ID   { get; set; }
        public string Type       { get; set; } = string.Empty;
        public string Data       { get; set; } = string.Empty;
        public string Created_At { get; set; } = string.Empty;

        public PlaythroughEvent ToEvent()
        {
            var type = Type switch
            {
                "director_input"    => EventType.DirectorInput,
                "narrative_output"  => EventType.NarrativeOutput,
                "state_change"      => EventType.StateChange,
                "memory_update"     => EventType.MemoryUpdate,
                "character_update"  => EventType.CharacterUpdate,
                "scene_change"      => EventType.SceneChange,
                "directive_change"  => EventType.DirectiveChange,
                "phase_transition"  => EventType.PhaseTransition,
                _                   => EventType.DirectorInput
            };

            return new PlaythroughEvent
            {
                ID        = ID,
                ProjectID = Project_ID,
                SessionID = Session_ID ?? 0,
                RoundID   = Round_ID,
                Type      = type,
                Data      = Data,
                CreatedAt = DateTime.Parse(Created_At)
            };
        }
    }
}
