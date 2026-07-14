using DirectorPrompt.Agents.Retrieval;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Infrastructure;
using DirectorPrompt.Infrastructure.Repositories;
using Xunit;

namespace DirectorPrompt.Tests;

public sealed class EmbeddingIndexServiceTests
{
    [Fact]
    public async Task SynchronizeProjectOnlyIndexesPendingEntries()
    {
        await using var context             = await DatabaseTestContext.CreateAsync();
        var             vectorManager       = new VectorTableManager(context.Scheduler);
        var             knowledgeRepository = new KnowledgeRepository(context.ConnectionFactory, context.Scheduler);
        var             memoryRepository    = new MemoryRepository(context.Scheduler);
        var group = await knowledgeRepository.CreateGroupAsync
                    (
                        new KnowledgeGroup { ProjectID = 1, Name = "group", Active = true }
                    );
        var first = await knowledgeRepository.CreateAsync
                    (
                        new KnowledgeEntry
                        {
                            ProjectID = 1,
                            GroupID   = group.ID,
                            Remarks   = "first",
                            Content   = "first content",
                            Active    = true
                        }
                    );
        await knowledgeRepository.CreateAsync
        (
            new KnowledgeEntry
            {
                ProjectID = 1,
                GroupID   = group.ID,
                Remarks   = "second",
                Content   = "second content",
                Active    = true
            }
        );
        var embeddingService = new DeterministicEmbeddingService();
        var indexService = new EmbeddingIndexService
        (
            knowledgeRepository,
            memoryRepository,
            new DeterministicEmbeddingServiceFactory(embeddingService)
        );
        var config = new ResolvedEmbeddingConfig
        {
            Provider  = "test",
            ModelName = "deterministic"
        };

        await indexService.SynchronizeProjectAsync(1, config);
        Assert.Equal(2, embeddingService.GeneratedTextCount);

        await indexService.SynchronizeProjectAsync(1, config);
        Assert.Equal(2, embeddingService.GeneratedTextCount);

        await knowledgeRepository.UpdateAsync(first with { Content = "changed content" });
        await indexService.SynchronizeProjectAsync(1, config);
        Assert.Equal(3, embeddingService.GeneratedTextCount);
    }
}
