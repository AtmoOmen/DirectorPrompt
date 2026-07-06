using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class SessionRepository : ISessionRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SessionRepository(SqliteConnectionFactory connectionFactory) =>
        this.connectionFactory = connectionFactory;

    public async Task<Session?> GetByIDAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<SessionRow>
                  (
                      "SELECT * FROM sessions WHERE id = @id",
                      new { id }
                  );

        return row?.ToSession();
    }

    public async Task<IReadOnlyList<Session>> GetByProjectAsync(long projectID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var rows = await connection.QueryAsync<SessionRow>
                   (
                       "SELECT * FROM sessions WHERE project_id = @projectID ORDER BY id DESC",
                       new { projectID }
                   );

        return rows.Select(r => r.ToSession()).ToList();
    }

    public async Task<Session> CreateAsync(Session session, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var now = DateTime.UtcNow.ToString("O");

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO sessions (project_id, title, created_at, updated_at)
                     VALUES (@projectID, @title, @createdAt, @updatedAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID = session.ProjectID,
                         title     = session.Title,
                         createdAt = now,
                         updatedAt = now
                     }
                 );

        return session with { ID = id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync("DELETE FROM sessions WHERE id = @id", new { id });
    }

    public async Task UpdateAsync(Session session, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        await connection.ExecuteAsync
        (
            """
            UPDATE sessions
            SET title = @title,
                updated_at = @updatedAt
            WHERE id = @id
            """,
            new
            {
                id        = session.ID,
                title     = session.Title,
                updatedAt = DateTime.UtcNow.ToString("O")
            }
        );
    }

    private sealed class SessionRow
    {
        public long   ID         { get; set; }
        public long   Project_ID { get; set; }
        public string Title      { get; set; } = string.Empty;
        public string Created_At { get; set; } = string.Empty;
        public string Updated_At { get; set; } = string.Empty;

        public Session ToSession() =>
            new()
            {
                ID        = ID,
                ProjectID = Project_ID,
                Title     = Title,
                CreatedAt = DateTime.Parse(Created_At),
                UpdatedAt = DateTime.Parse(Updated_At)
            };
    }
}
