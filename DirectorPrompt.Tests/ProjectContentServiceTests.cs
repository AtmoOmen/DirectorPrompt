using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Infrastructure.Repositories;
using DirectorPrompt.Services;

namespace DirectorPrompt.Tests;

public sealed class ProjectContentServiceTests
{
    [Fact]
    public async Task ChangeBatchCoalescesNotificationsForSameProject()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var service = new ProjectContentService(context.Scheduler, new ProjectRepository(context.Scheduler));
        var changes = new List<ProjectContentChange>();
        service.Changed += changes.Add;

        using (service.BeginChangeBatch())
        {
            service.NotifyProjectChanged(1);
            service.NotifyProjectChanged(1);
        }

        Assert.Single(changes);
        Assert.Equal(1, changes[0].ProjectID);
        Assert.False(changes[0].IsDeleted);
    }

    [Fact]
    public async Task UpdatingKnowledgeMetadataPreservesEmbeddingState()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var service = new ProjectContentService(context.Scheduler, new ProjectRepository(context.Scheduler));
        var entry = await service.ManageKnowledgeEntryAsync
        (
            1,
            ProjectContentAction.Create,
            new KnowledgeEntry
            {
                ProjectID = 1,
                Remarks = "原备注",
                Content = "内容",
                Keywords = ["关键字"],
                Active = true
            },
            null
        );

        await context.Scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
                                      UPDATE knowledge_entries
                                      SET content_hash = 'indexed', embedding_fingerprint = 'fingerprint'
                                      WHERE id = $id
                                      """;
                command.Parameters.AddWithValue("$id", entry.ID);
                await command.ExecuteNonQueryAsync(token);
            }
        );

        await service.ManageKnowledgeEntryAsync
        (
            1,
            ProjectContentAction.Update,
            entry with { Remarks = "新备注" },
            entry.ID
        );

        var contentHash = await context.Scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT content_hash FROM knowledge_entries WHERE id = $id";
                command.Parameters.AddWithValue("$id", entry.ID);
                return (string?)await command.ExecuteScalarAsync(token);
            }
        );

        Assert.Equal("indexed", contentHash);
    }

    [Fact]
    public async Task DeletingKnowledgeGroupDeletesEntriesAndPhaseReferences()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var service = new ProjectContentService(context.Scheduler, new ProjectRepository(context.Scheduler));
        var created = await service.CreateProjectAsync
        (
            "项目",
            "设定",
            "开场白",
            new ProjectBlueprint
            {
                KnowledgeGroups =
                [
                    new KnowledgeGroupDefinition
                    {
                        Key = "lore",
                        Name = "设定",
                        Entries =
                        [
                            new KnowledgeEntryDefinition
                            {
                                Key = "entry",
                                Remarks = "条目",
                                Content = "内容"
                            }
                        ]
                    }
                ],
                StateAttributes =
                [
                    new StateAttributeDefinition
                    {
                        Name = "progress",
                        DisplayName = "进度",
                        Numeric = new NumericStateDefinition(),
                        Phases =
                        [
                            new PhaseDefinition
                            {
                                Name = "阶段",
                                Expression = "{val} > 0",
                                KnowledgeGroupKeys = ["lore"],
                                KnowledgeEntryKeys = ["entry"]
                            }
                        ]
                    }
                ]
            },
            false
        );

        await service.ManageKnowledgeGroupAsync
        (
            created.Project.ID,
            ProjectContentAction.Delete,
            null,
            created.GroupIDs["lore"]
        );
        var snapshot = await service.GetProjectAsync(created.Project.ID);

        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.KnowledgeGroups);
        Assert.Empty(snapshot.UngroupedKnowledgeEntries);
        Assert.Empty(snapshot.StateAttributes.Single().Configuration.Phases.Single().KnowledgeIDs);
        Assert.Empty(snapshot.StateAttributes.Single().Configuration.Phases.Single().KnowledgeGroupIDs);
    }

    [Fact]
    public async Task DeletingStateAttributeRemovesDependentEnumTransitions()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var service = new ProjectContentService(context.Scheduler, new ProjectRepository(context.Scheduler));
        var created = await service.CreateProjectAsync
        (
            "项目",
            string.Empty,
            string.Empty,
            new ProjectBlueprint
            {
                StateAttributes =
                [
                    new StateAttributeDefinition
                    {
                        Name = "score",
                        DisplayName = "分数",
                        ValueType = StateValueType.Numeric,
                        Driver = Driver.Narrative,
                        Numeric = new NumericStateDefinition()
                    },
                    new StateAttributeDefinition
                    {
                        Name = "weather",
                        DisplayName = "天气",
                        ValueType = StateValueType.Enum,
                        Enumeration = new EnumStateDefinition
                        {
                            Options = ["晴"],
                            Transitions =
                            [
                                new EnumTransitionConfig
                                {
                                    Option = "晴",
                                    AttributeName = "score",
                                    Method = EnumTransitionMethod.Expression,
                                    Expression = "{val} > 0"
                                }
                            ]
                        }
                    }
                ]
            },
            false
        );
        var snapshot = await service.GetProjectAsync(created.Project.ID);
        var score = snapshot!.StateAttributes.Single(attribute => attribute.Name == "score");

        await service.ManageStateAttributeAsync
        (
            created.Project.ID,
            ProjectContentAction.Delete,
            null,
            score.ID
        );
        snapshot = await service.GetProjectAsync(created.Project.ID);

        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.StateAttributes.Single().Configuration.Transitions!);
    }
}
