using Dapper;
using DirectorPrompt.Domain.Models;

namespace DirectorPrompt.Infrastructure.Repositories;

// TODO: 好像没有用到, 确定一下
public sealed class SidebarSnapshotRepository
(
    SQLiteDatabaseScheduler databaseScheduler
)
{
    public Task<SidebarSnapshot> GetAsync
    (
        long              projectID,
        long              sessionID,
        CancellationToken cancellationToken = default
    ) =>
        databaseScheduler.ExecuteReadAsync
        (
            async (connection, token) =>
            {
                const string SQL =
                    """
                    SELECT time_label
                    FROM scenes
                    WHERE session_id = @sessionID AND status = 'Active'
                    ORDER BY id DESC
                    LIMIT 1;

                    SELECT sa.display_name AS Name,
                           COALESCE(sv.value, '—') AS Value
                    FROM state_attributes sa
                    LEFT JOIN state_values sv
                      ON sv.attribute_id = sa.id AND sv.session_id = @sessionID
                    WHERE sa.project_id = @projectID AND sa.scope = 'Global'
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

                await using var grid = await connection.QueryMultipleAsync(command);

                var sceneLabel = await grid.ReadFirstOrDefaultAsync<string>();
                var states     = (await grid.ReadAsync<SidebarStateItem>()).ToList();
                var directives = (await grid.ReadAsync<ActiveDirective>()).ToList();
                return new SidebarSnapshot(sceneLabel, states, directives);
            },
            cancellationToken: cancellationToken
        );
}
