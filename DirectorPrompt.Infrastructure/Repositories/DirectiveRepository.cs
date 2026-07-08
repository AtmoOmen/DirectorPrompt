using System.Text.Json;
using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class DirectiveRepository : IDirectiveRepository
{
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly IRoundChangeRepository  roundChangeRepository;

    public DirectiveRepository(SqliteConnectionFactory connectionFactory, IRoundChangeRepository roundChangeRepository)
    {
        this.connectionFactory     = connectionFactory;
        this.roundChangeRepository = roundChangeRepository;
    }

    public async Task<IReadOnlyList<ActiveDirective>> GetActiveAsync(long sessionID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<ActiveDirectiveRow>
                   (
                       """
                       SELECT * FROM active_directives
                       WHERE session_id = @sessionID
                         AND (ttl IS NULL OR ttl > 0)
                       ORDER BY id
                       """,
                       new { sessionID }
                   );

        return rows.Select(r => r.ToActiveDirective()).ToList();
    }

    public async Task<ActiveDirective> AddAsync(ActiveDirective directive, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO active_directives (project_id, session_id, type, content, ttl, created_at)
                     VALUES (@projectID, @sessionID, @type, @content, @ttl, @createdAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID = directive.ProjectID,
                         sessionID = directive.SessionID,
                         type      = JsonNamingPolicy.SnakeCaseLower.ConvertName(directive.Type.ToString()),
                         content   = directive.Content,
                         ttl       = directive.TTL,
                         createdAt = directive.CreatedAt.ToString("O")
                     }
                 );

        await roundChangeRepository.RecordCreateAsync(RoundContext.Current ?? 0, "active_directives", id, cancellationToken: cancellationToken);

        return directive with { ID = id };
    }

    public async Task RemoveAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var oldRow = await connection.QueryFirstOrDefaultAsync<IDictionary<string, object>>
                     (
                         "SELECT * FROM active_directives WHERE id = @id",
                         new { id }
                     );

        await connection.ExecuteAsync("DELETE FROM active_directives WHERE id = @id", new { id });

        if (oldRow is not null)
            await roundChangeRepository.RecordDeleteAsync(RoundContext.Current ?? 0, "active_directives", id, JsonSerializer.Serialize(oldRow), cancellationToken);
    }

    public async Task<IReadOnlyList<ActiveDirective>> DecrementTTLAsync(long sessionID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var affectedRows = await connection.QueryAsync
                           (
                               """
                               SELECT id, ttl FROM active_directives
                               WHERE session_id = @sessionID AND ttl IS NOT NULL
                               """,
                               new { sessionID }
                           );

        foreach (var row in affectedRows)
        {
            var oldTTL  = (long)row.ttl;
            var oldData = JsonSerializer.Serialize(new { id = (long)row.id, ttl = oldTTL });

            await connection.ExecuteAsync
            (
                "UPDATE active_directives SET ttl = ttl - 1 WHERE id = @id",
                new { id = (long)row.id }
            );

            await roundChangeRepository.RecordUpdateAsync
            (
                RoundContext.Current ?? 0,
                "active_directives",
                (long)row.id,
                oldData,
                cancellationToken
            );
        }

        var expiredRows = await connection.QueryAsync
                          (
                              """
                              SELECT * FROM active_directives
                              WHERE session_id = @sessionID AND ttl IS NOT NULL AND ttl <= 0
                              """,
                              new { sessionID }
                          );

        await connection.ExecuteAsync
        (
            "DELETE FROM active_directives WHERE session_id = @sessionID AND ttl IS NOT NULL AND ttl <= 0",
            new { sessionID }
        );

        foreach (var row in expiredRows)
        {
            await roundChangeRepository.RecordDeleteAsync
            (
                RoundContext.Current ?? 0,
                "active_directives",
                (long)row.id,
                JsonSerializer.Serialize(row),
                cancellationToken
            );
        }

        var rows = await connection.QueryAsync<ActiveDirectiveRow>
                   (
                       """
                       SELECT * FROM active_directives
                       WHERE session_id = @sessionID
                         AND (ttl IS NULL OR ttl > 0)
                       ORDER BY id
                       """,
                       new { sessionID }
                   );

        return rows.Select(r => r.ToActiveDirective()).ToList();
    }

    private sealed class ActiveDirectiveRow
    {
        public long   ID         { get; set; }
        public long   Project_ID { get; set; }
        public long?  Session_ID { get; set; }
        public string Type       { get; set; } = "plot";
        public string Content    { get; set; } = string.Empty;
        public int?   TTL        { get; set; }
        public string Created_At { get; set; } = string.Empty;

        public ActiveDirective ToActiveDirective() =>
            new()
            {
                ID        = ID,
                ProjectID = Project_ID,
                SessionID = Session_ID ?? 0,
                Type = Type switch
                {
                    "tone"                 => DirectiveType.Tone,
                    "temporary_constraint" => DirectiveType.TemporaryConstraint,
                    "scene_change"         => DirectiveType.SceneChange,
                    _                      => DirectiveType.Plot
                },
                Content   = Content,
                TTL       = TTL,
                CreatedAt = DateTime.Parse(Created_At)
            };
    }
}
