using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class DirectiveRepository : IDirectiveRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public DirectiveRepository(SqliteConnectionFactory connectionFactory) =>
        this.connectionFactory = connectionFactory;

    public async Task<IReadOnlyList<ActiveDirective>> GetActiveAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<ActiveDirectiveRow>
                   (
                       """
                       SELECT * FROM active_directives
                       WHERE project_id = @projectID
                         AND (ttl IS NULL OR ttl > 0)
                       ORDER BY id
                       """,
                       new { projectID }
                   );

        return rows.Select(r => r.ToActiveDirective()).ToList();
    }

    public async Task<ActiveDirective> AddAsync(ActiveDirective directive, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO active_directives (project_id, type, content, ttl, created_at)
                     VALUES (@projectID, @type, @content, @ttl, @createdAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID = directive.ProjectID,
                         type      = directive.Type.ToString().ToLowerInvariant(),
                         content   = directive.Content,
                         ttl       = directive.TTL,
                         createdAt = directive.CreatedAt.ToString("O")
                     }
                 );

        return directive with { ID = id };
    }

    public async Task RemoveAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync("DELETE FROM active_directives WHERE id = @id", new { id });
    }

    public async Task<IReadOnlyList<ActiveDirective>> DecrementTTLAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection  = await connectionFactory.CreateAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            "UPDATE active_directives SET ttl = ttl - 1 WHERE project_id = @projectID AND ttl IS NOT NULL",
            new { projectID },
            transaction
        );

        await connection.ExecuteAsync
        (
            "DELETE FROM active_directives WHERE project_id = @projectID AND ttl IS NOT NULL AND ttl <= 0",
            new { projectID },
            transaction
        );

        var rows = await connection.QueryAsync<ActiveDirectiveRow>
                   (
                       """
                       SELECT * FROM active_directives
                       WHERE project_id = @projectID
                         AND (ttl IS NULL OR ttl > 0)
                       ORDER BY id
                       """,
                       new { projectID },
                       transaction
                   );

        await transaction.CommitAsync(cancellationToken);

        return rows.Select(r => r.ToActiveDirective()).ToList();
    }

    private sealed class ActiveDirectiveRow
    {
        public long   ID         { get; set; }
        public long   Project_ID { get; set; }
        public string Type       { get; set; } = "plot";
        public string Content    { get; set; } = string.Empty;
        public int?   TTL        { get; set; }
        public string Created_At { get; set; } = string.Empty;

        public ActiveDirective ToActiveDirective() =>
            new()
            {
                ID        = ID,
                ProjectID = Project_ID,
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
