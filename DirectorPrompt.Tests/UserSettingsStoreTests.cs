using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Infrastructure;

namespace DirectorPrompt.Tests;

public sealed class UserSettingsStoreTests
{
    [Fact]
    public void MigrationRemovesObsoleteTasksAndPreservesModelsAndPrompts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"directorprompt-settings-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText
            (
                path,
                """
                {
                  "orchestrator": {
                    "providers": [],
                    "models": [
                      { "id": "model", "displayName": "模型" }
                    ],
                    "prompts": [
                      { "id": "prompt", "displayName": "提示词", "content": "内容" }
                    ],
                    "agentTasks": [
                      { "taskType": "Knowledge", "modelConfigID": "model" },
                      { "taskType": "MemoryRecall", "modelConfigID": "model" },
                      { "taskType": "Narrator", "modelConfigID": "model", "promptID": "prompt" }
                    ]
                  }
                }
                """
            );
            var store = new UserSettingsStore(path);

            Assert.True(store.MigrateIfNeeded());

            var settings = store.Load();

            Assert.Single(settings.Orchestrator.AgentTasks);
            Assert.Equal(AgentTaskType.Narrator, settings.Orchestrator.AgentTasks.Single().TaskType);
            Assert.Single(settings.Orchestrator.Models);
            Assert.Single(settings.Orchestrator.Prompts);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
