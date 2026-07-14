using DirectorPrompt.Agents;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Infrastructure.Repositories;
using Xunit;

namespace DirectorPrompt.Tests;

public sealed class HistoryBuilderTests
{
    [Fact]
    public async Task BuildAsyncKeepsOnlyConfiguredRecentRounds()
    {
        await using var context    = await DatabaseTestContext.CreateAsync();
        var             repository = new EventRepository(context.Scheduler);

        for (var roundID = 1; roundID <= 100; roundID++)
            await repository.AppendBatchAsync
            (
                [
                    CreateEvent(roundID, EventType.DirectorInput,   $"[{{\"type\":\"Plot\",\"content\":\"input-{roundID}\",\"order\":1}}]"),
                    CreateEvent(roundID, EventType.NarrativeOutput, $"output-{roundID}")
                ]
            );

        var config = new OrchestratorConfig
        {
            HistoryContext = new HistoryContextConfig { MaxRounds = 40, TokenBudget = 100000 }
        };
        var history = await new HistoryBuilder(repository, config).BuildAsync(1, 1, 101);

        Assert.Equal(40,  history.Count);
        Assert.Equal(61,  history[0].RoundID);
        Assert.Equal(100, history[^1].RoundID);
    }

    private static PlaythroughEvent CreateEvent(long roundID, EventType type, string data) =>
        new()
        {
            ProjectID = 1,
            SessionID = 1,
            SceneID   = 1,
            RoundID   = roundID,
            Type      = type,
            Data      = data,
            CreatedAt = DateTime.UtcNow
        };
}
