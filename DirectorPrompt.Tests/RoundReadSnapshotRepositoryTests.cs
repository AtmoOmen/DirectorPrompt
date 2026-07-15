using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Infrastructure.Repositories;
using Xunit;

namespace DirectorPrompt.Tests;

public sealed class RoundReadSnapshotRepositoryTests
{
    [Fact]
    public async Task SnapshotReadsRoundContextFromOneConsistentView()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        await context.Scheduler.ExecuteAsync
        (async (connection, token) =>
            {
                await connection.ExecuteAsync
                (
                    new CommandDefinition
                    (
                        """
                        INSERT INTO scenes
                        (id, project_id, session_id, timeline_position, time_label, status)
                        VALUES (1, 1, 1, 1000, 'night', 'Active');

                        INSERT INTO state_attributes
                        (id, project_id, name, display_name, scope, value_type, driver, config)
                        VALUES
                        (1, 1, 'weather', 'Weather', 'Global', 'Enum', 'System', '{}'),
                        (2, 1, 'mood', 'Mood', 'Category', 'Enum', 'Narrative', '{}');

                        INSERT INTO state_values
                        (attribute_id, session_id, value, updated_at)
                        VALUES (1, 1, 'rain', '2026-01-01T00:00:00Z');

                        INSERT INTO active_directives
                        (id, project_id, session_id, type, content, ttl, created_at)
                        VALUES (1, 1, 1, 'Tone', 'quiet', 2, '2026-01-01T00:00:00Z');

                        INSERT INTO characters
                        (id, project_id, session_id, name, description, aliases, category_ids, status, touch_count, last_touched_round, created_at, updated_at)
                        VALUES
                        (1, 1, 1, 'A', '', '[]', '[]', 'Active', 1, 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
                        (2, 1, 1, 'B', '', '[]', '[]', 'Active', 1, 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');

                        INSERT INTO character_scene_presence
                        (character_id, scene_id)
                        VALUES (1, 1);

                        INSERT INTO character_state_values
                        (character_id, attribute_id, value, updated_at)
                        VALUES (1, 2, 'calm', '2026-01-01T00:00:00Z');

                        INSERT INTO character_relations
                        (id, project_id, session_id, source_character_id, target_character_id, relation_type, description, created_at, updated_at)
                        VALUES (1, 1, 1, 1, 2, 'friend', '', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                        """,
                        cancellationToken: token
                    )
                );
            }
        );
        var repository = new RoundReadSnapshotRepository(context.Scheduler);

        var snapshot = await repository.GetAsync(1, 1, 1);

        Assert.Equal("night", snapshot.Scene?.TimeLabel);
        Assert.Single(snapshot.GlobalAttributes);
        Assert.Single(snapshot.GlobalValues);
        Assert.Equal("rain", snapshot.GlobalValues[0].Value);
        Assert.Single(snapshot.ActiveDirectives);
        Assert.Equal(DirectiveType.Tone, snapshot.ActiveDirectives[0].Type);
        Assert.Single(snapshot.SceneCharacters);
        Assert.Single(snapshot.CharacterAttributes);
        Assert.Single(snapshot.CharacterValues);
        Assert.Single(snapshot.CharacterRelations);
    }
}
