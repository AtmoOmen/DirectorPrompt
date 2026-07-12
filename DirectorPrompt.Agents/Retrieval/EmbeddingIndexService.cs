using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using Serilog;

namespace DirectorPrompt.Agents.Retrieval;

public sealed class EmbeddingIndexService
(
    IKnowledgeRepository     knowledgeRepository,
    IMemoryRepository        memoryRepository,
    IEmbeddingServiceFactory embeddingServiceFactory
)
{
    private const string KNOWLEDGE_INDEX_VERSION = "knowledge-v3";
    private const string MEMORY_INDEX_VERSION    = "memory-v3";

    public async Task SynchronizeProjectAsync
    (
        long                    projectID,
        ResolvedEmbeddingConfig embeddingConfig,
        CancellationToken       cancellationToken = default
    )
    {
        var knowledgeTask = knowledgeRepository.GetByProjectAsync(projectID, cancellationToken);
        var memoryTask    = memoryRepository.GetByProjectAsync(projectID, cancellationToken);

        await Task.WhenAll(knowledgeTask, memoryTask);

        await Task.WhenAll
        (
            IndexKnowledgeAsync(await knowledgeTask, embeddingConfig, cancellationToken),
            IndexMemoriesAsync(await memoryTask, embeddingConfig, cancellationToken)
        );
    }

    public async Task IndexKnowledgeAsync
    (
        IReadOnlyList<KnowledgeEntry> entries,
        ResolvedEmbeddingConfig       embeddingConfig,
        CancellationToken             cancellationToken = default
    )
    {
        var staleEntries = entries
                           .Select(e => (entry: e, texts: BuildKnowledgeTexts(e)))
                           .Where(x => x.entry.ContentHash != ComputeHash(x.texts, embeddingConfig.Fingerprint, KNOWLEDGE_INDEX_VERSION))
                           .ToList();

        if (staleEntries.Count == 0)
            return;

        Log.Information("知识向量同步: 需生成 {Count}/{Total} 条", staleEntries.Count, entries.Count);

        var embeddingService = embeddingServiceFactory.Create(embeddingConfig);
        var allTexts         = staleEntries.SelectMany(x => x.texts.Select(t => t.text)).ToList();
        var embeddings       = await embeddingService.GenerateEmbeddingsAsync(allTexts, cancellationToken);
        var offset           = 0;

        foreach (var (entry, texts) in staleEntries)
        {
            var vectors = new List<(string source, byte[] embedding)>();

            for (var i = 0; i < texts.Count; i++)
            {
                vectors.Add
                (
                    (
                        texts[i].source,
                        EmbeddingConversions.FloatsToBytes(embeddings[offset + i])
                    )
                );
            }

            var hash = ComputeHash(texts, embeddingConfig.Fingerprint, KNOWLEDGE_INDEX_VERSION);
            await knowledgeRepository.SaveEmbeddingsAsync(entry.ProjectID, entry.ID, vectors, hash, cancellationToken);
            offset += texts.Count;
        }
    }

    public async Task IndexMemoriesAsync
    (
        IReadOnlyList<MemoryEntry> entries,
        ResolvedEmbeddingConfig    embeddingConfig,
        CancellationToken          cancellationToken = default
    )
    {
        var staleEntries = entries
                           .Select(e => (entry: e, texts: BuildMemoryTexts(e)))
                           .Where(x => x.entry.ContentHash != ComputeHash(x.texts, embeddingConfig.Fingerprint, MEMORY_INDEX_VERSION))
                           .ToList();

        if (staleEntries.Count == 0)
            return;

        Log.Information("记忆向量同步: 需生成 {Count}/{Total} 条", staleEntries.Count, entries.Count);

        var embeddingService = embeddingServiceFactory.Create(embeddingConfig);
        var allTexts         = staleEntries.SelectMany(x => x.texts.Select(t => t.text)).ToList();
        var embeddings       = await embeddingService.GenerateEmbeddingsAsync(allTexts, cancellationToken);
        var offset           = 0;

        foreach (var (entry, texts) in staleEntries)
        {
            var vectors = new List<(string source, byte[] embedding)>();

            for (var i = 0; i < texts.Count; i++)
            {
                vectors.Add
                (
                    (
                        texts[i].source,
                        EmbeddingConversions.FloatsToBytes(embeddings[offset + i])
                    )
                );
            }

            var hash = ComputeHash(texts, embeddingConfig.Fingerprint, MEMORY_INDEX_VERSION);
            await memoryRepository.SaveEmbeddingsAsync(entry.ProjectID, entry.ID, vectors, hash, cancellationToken);
            offset += texts.Count;
        }
    }

    private static IReadOnlyList<(string source, string text)> BuildKnowledgeTexts(KnowledgeEntry entry)
    {
        var texts = new List<(string source, string text)>();

        foreach (var keyword in entry.Keywords)
        {
            var normalized = keyword.Trim();

            if (normalized.Length > 0)
                texts.Add(($"keyword:{normalized}", normalized));
        }

        if (!string.IsNullOrWhiteSpace(entry.Content))
            texts.Add(("content", entry.Content));

        return texts;
    }

    private static IReadOnlyList<(string source, string text)> BuildMemoryTexts(MemoryEntry entry)
    {
        var texts = new List<(string source, string text)>();

        foreach (var tag in entry.Tags)
        {
            var normalized = tag.Trim();

            if (normalized.Length > 0)
                texts.Add(($"tag:{normalized}", normalized));
        }

        if (!string.IsNullOrWhiteSpace(entry.Content))
            texts.Add(("content", entry.Content));

        return texts;
    }

    private static string ComputeHash
    (
        IReadOnlyList<(string source, string text)> texts,
        string                                       fingerprint,
        string                                       indexVersion
    )
    {
        var combined = string.Join('\0', texts.Select(t => t.text));
        return EmbeddingConversions.ComputeHash(combined, $"{fingerprint}|{indexVersion}");
    }
}
