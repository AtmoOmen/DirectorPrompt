using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Infrastructure.Repositories;
using DirectorPrompt.Services;

namespace DirectorPrompt.Tests;

public sealed class ProjectContentServiceTests
{
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
