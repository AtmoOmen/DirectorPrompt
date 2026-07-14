using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class SidebarSnapshotRepository
(
    SqliteDatabaseScheduler databaseScheduler
)
{
    public Task<SidebarSnapshot> GetAsync
    (
        long              projectID,
        long              sessionID,
        CancellationToken cancellationToken = default
    ) =>
        databaseScheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                const string SQL =
                    """
                    SELECT time_label
                    FROM scenes
                    WHERE session_id = @sessionID AND status = 'active'
                    ORDER BY id DESC
                    LIMIT 1;

                    SELECT sa.display_name AS Name,
                           COALESCE(sv.value, '—') AS Value
                    FROM state_attributes sa
                    LEFT JOIN state_values sv
                      ON sv.attribute_id = sa.id AND sv.session_id = @sessionID
                    WHERE sa.project_id = @projectID AND sa.scope = 'global'
                    ORDER BY sa.id;

                    SELECT id, project_id, session_id, type, content, ttl, created_at
                    FROM active_directives
                    WHERE session_id = @sessionID AND (ttl IS NULL OR ttl > 0)
                    ORDER BY id;
                    """;
                var command = new CommandDefinition
                (
                    SQL,
                    new { projectID, sessionID },
                    cancellationToken: token
                );
                using var grid       = await connection.QueryMultipleAsync(command);
                var       sceneLabel = await grid.ReadFirstOrDefaultAsync<string>();
                var       states     = (await grid.ReadAsync<SidebarStateItem>()).ToList();
                var directives = (await grid.ReadAsync<DirectiveRow>())
                                 .Select(row => row.ToActiveDirective())
                                 .ToList();
                return new SidebarSnapshot(sceneLabel, states, directives);
            },
            cancellationToken: cancellationToken
        );

    private sealed class DirectiveRow
    {
        public long ID { get; set; }

        public long Project_ID { get; set; }

        public long Session_ID { get; set; }

        public string Type { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public int? TTL { get; set; }

        public string Created_At { get; set; } = string.Empty;

        public ActiveDirective ToActiveDirective() =>
            new()
            {
                ID        = ID,
                ProjectID = Project_ID,
                SessionID = Session_ID,
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
