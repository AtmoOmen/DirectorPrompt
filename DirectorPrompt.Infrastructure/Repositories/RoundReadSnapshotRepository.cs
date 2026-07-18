using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class RoundReadSnapshotRepository
(
    SQLiteDatabaseScheduler scheduler
) : IRoundReadSnapshotRepository
{
    public Task<RoundReadSnapshot> GetAsync
    (
        long              projectID,
        long              sessionID,
        long?             sceneID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteReadAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var scene = sceneID is null ?
                                null :
                                await connection.QueryFirstOrDefaultAsync<Scene>
                                (
                                    new CommandDefinition
                                    (
                                        "SELECT * FROM scenes WHERE id = @sceneID AND session_id = @sessionID",
                                        new { sceneID, sessionID },
                                        transaction,
                                        cancellationToken: token
                                    )
                                );
                var attributes = (await connection.QueryAsync<StateAttribute>
                                  (
                                      new CommandDefinition
                                      (
                                          "SELECT * FROM state_attributes WHERE project_id = @projectID AND scope IN ('Global', 'Category') ORDER BY id",
                                          new { projectID },
                                          transaction,
                                          cancellationToken: token
                                      )
                                  )).ToList();
                var globalValues = (await connection.QueryAsync<StateValue>
                                    (
                                        new CommandDefinition
                                        (
                                            """
                                            SELECT sv.*
                                            FROM state_values sv
                                            JOIN state_attributes sa ON sa.id = sv.attribute_id
                                            WHERE sa.project_id = @projectID
                                              AND sa.scope = 'Global'
                                              AND sv.session_id = @sessionID
                                            ORDER BY sv.attribute_id
                                            """,
                                            new { projectID, sessionID },
                                            transaction,
                                            cancellationToken: token
                                        )
                                    )).ToList();
                var directives = (await connection.QueryAsync<ActiveDirective>
                                  (
                                      new CommandDefinition
                                      (
                                          """
                                          SELECT *
                                          FROM active_directives
                                          WHERE session_id = @sessionID
                                            AND (ttl IS NULL OR ttl > 0)
                                          ORDER BY id
                                          """,
                                          new { sessionID },
                                          transaction,
                                          cancellationToken: token
                                      )
                                  )).ToList();
                IReadOnlyList<Character>           characters      = [];
                IReadOnlyList<CharacterStateValue> characterValues = [];
                IReadOnlyList<CharacterRelation>   relations       = [];

                if (sceneID is not null)
                {
                    characters = (await connection.QueryAsync<Character>
                                  (
                                      new CommandDefinition
                                      (
                                          """
                                          SELECT c.*
                                          FROM characters c
                                          JOIN character_scene_presence p ON p.character_id = c.id
                                          WHERE p.scene_id = @sceneID
                                            AND c.session_id = @sessionID
                                            AND c.status = 'Active'
                                          ORDER BY c.id
                                          """,
                                          new { sceneID, sessionID },
                                          transaction,
                                          cancellationToken: token
                                      )
                                  )).ToList();
                    var characterIDs = characters.Select(character => character.ID).ToList();

                    if (characterIDs.Count > 0)
                    {
                        characterValues = (await connection.QueryAsync<CharacterStateValue>
                                           (
                                               new CommandDefinition
                                               (
                                                   "SELECT * FROM character_state_values WHERE character_id IN @characterIDs",
                                                   new { characterIDs },
                                                   transaction,
                                                   cancellationToken: token
                                               )
                                           )).ToList();
                        relations = (await connection.QueryAsync<CharacterRelation>
                                     (
                                         new CommandDefinition
                                         (
                                             """
                                             SELECT *
                                             FROM character_relations
                                             WHERE session_id = @sessionID
                                               AND
                                               (
                                                   source_character_id IN @characterIDs
                                                   OR target_character_id IN @characterIDs
                                               )
                                             ORDER BY id
                                             """,
                                             new { sessionID, characterIDs },
                                             transaction,
                                             cancellationToken: token
                                         )
                                     )).ToList();
                    }
                }

                await transaction.CommitAsync(token);

                return new RoundReadSnapshot
                {
                    Scene               = scene,
                    GlobalAttributes    = attributes.Where(attribute => attribute.Scope == StateScope.Global).ToList(),
                    GlobalValues        = globalValues,
                    ActiveDirectives    = directives,
                    SceneCharacters     = characters,
                    CharacterAttributes = attributes.Where(attribute => attribute.Scope == StateScope.Category).ToList(),
                    CharacterValues     = characterValues,
                    CharacterRelations  = relations
                };
            },
            cancellationToken: cancellationToken
        );
}
