using DirectorPrompt.Agents.Config;
using DirectorPrompt.Agents.Retrieval;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using DirectorPrompt.Domain.Services;
using DirectorPrompt.Services;
using ModelContextProtocol.Server;
using Serilog;

namespace DirectorPrompt.MCP;

public sealed class MCPProjectTools
(
    IProjectContentService          projectContentService,
    IProjectPortService             projectPortService,
    IProjectEditWindowCoordinator  projectEditWindowCoordinator,
    IKnowledgeRepository            knowledgeRepository,
    EmbeddingIndexService           embeddingIndexService,
    AgentConfigResolver             agentConfigResolver,
    UserSettings                    userSettings
)
{
    [McpServerTool(Name = "list_projects")]
    public Task<MCPToolResponse> ListProjectsAsync(CancellationToken cancellationToken) =>
        ExecuteAsync
        (
            "list_projects",
            null,
            async () => await projectContentService.ListProjectsAsync(cancellationToken)
        );

    [McpServerTool(Name = "get_project")]
    public Task<MCPToolResponse> GetProjectAsync(long projectID, CancellationToken cancellationToken) =>
        ExecuteAsync
        (
            "get_project",
            new { projectID },
            async () =>
            {
                var snapshot = await projectContentService.GetProjectAsync(projectID, cancellationToken);

                if (snapshot is null)
                    throw new InvalidOperationException($"项目不存在: ID={projectID}");

                return snapshot;
            }
        );

    [McpServerTool(Name = "create_project")]
    public Task<MCPToolResponse> CreateProjectAsync
    (
        string            name,
        string            description,
        string            openingMessage,
        ProjectBlueprint? blueprint = null,
        bool              dryRun = false,
        CancellationToken cancellationToken = default
    ) =>
        ExecuteAsync
        (
            "create_project",
            new { name, description, openingMessage, blueprint, dryRun },
            async () =>
            {
                var result = await projectContentService.CreateProjectAsync
                (
                    name,
                    description,
                    openingMessage,
                    blueprint,
                    dryRun,
                    cancellationToken
                );

                if (dryRun)
                    return result;

                var indexStatus = await TryIndexKnowledgeAsync(result.Project.ID, cancellationToken);
                return result with { IndexStatus = indexStatus };
            }
        );

    [McpServerTool(Name = "update_project")]
    public Task<MCPToolResponse> UpdateProjectAsync
    (
        long              projectID,
        string            name,
        string            description,
        string            openingMessage,
        CancellationToken cancellationToken = default
    ) =>
        ExecuteAsync
        (
            "update_project",
            new { projectID, name, description, openingMessage },
            async () =>
            {
                await projectEditWindowCoordinator.CloseForExternalChangeAsync(projectID);
                return await projectContentService.UpdateProjectAsync
                (
                    new Project
                    {
                        ID = projectID,
                        Name = name,
                        Description = description,
                        OpeningMessage = openingMessage
                    },
                    cancellationToken
                );
            }
        );

    [McpServerTool(Name = "delete_project")]
    public Task<MCPToolResponse> DeleteProjectAsync(long projectID, CancellationToken cancellationToken = default) =>
        ExecuteAsync
        (
            "delete_project",
            new { projectID },
            async () =>
            {
                await projectEditWindowCoordinator.CloseForExternalChangeAsync(projectID);
                return await projectContentService.DeleteProjectAsync(projectID, cancellationToken);
            }
        );

    [McpServerTool(Name = "import_project")]
    public Task<MCPToolResponse> ImportProjectAsync
    (
        string              sourcePath,
        ProjectImportFormat format,
        CancellationToken   cancellationToken = default
    ) =>
        ExecuteAsync
        (
            "import_project",
            new { sourcePath, format },
            async () =>
            {
                var path = ValidateLocalPath(sourcePath, nameof(sourcePath));

                if (!File.Exists(path))
                    throw new FileNotFoundException("导入文件不存在", path);

                var result = format switch
                {
                    ProjectImportFormat.DirectorPromptPackage => await projectPortService.ImportAsync(path, cancellationToken),
                    ProjectImportFormat.SillyTavernCharacterCard => await projectPortService.ImportSillyTavernAsync(path, cancellationToken),
                    _ => throw new ArgumentOutOfRangeException(nameof(format))
                };

                projectContentService.NotifyProjectChanged(result.ProjectID);
                return result;
            }
        );

    [McpServerTool(Name = "export_project")]
    public Task<MCPToolResponse> ExportProjectAsync
    (
        long              projectID,
        string            destinationPath,
        bool              overwrite,
        CancellationToken cancellationToken = default
    ) =>
        ExecuteAsync
        (
            "export_project",
            new { projectID, destinationPath, overwrite },
            async () =>
            {
                var path = ValidateLocalPath(destinationPath, nameof(destinationPath));

                if (File.Exists(path) && !overwrite)
                    throw new IOException($"导出目标已存在: {path}");

                var directory = Path.GetDirectoryName(path);

                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    throw new DirectoryNotFoundException($"导出目录不存在: {directory}");

                if (File.Exists(path))
                    File.Delete(path);

                await projectPortService.ExportAsync(projectID, path, cancellationToken);
                return new { projectID, destinationPath = path };
            }
        );

    [McpServerTool(Name = "manage_knowledge_group")]
    public Task<MCPToolResponse> ManageKnowledgeGroupAsync
    (
        long                 projectID,
        ProjectContentAction action,
        KnowledgeGroup?      group = null,
        long?                groupID = null,
        CancellationToken    cancellationToken = default
    ) =>
        ExecuteAsync
        (
            "manage_knowledge_group",
            new { projectID, action, group, groupID },
            async () =>
            {
                await projectEditWindowCoordinator.CloseForExternalChangeAsync(projectID);
                return await projectContentService.ManageKnowledgeGroupAsync
                (
                    projectID,
                    action,
                    group,
                    groupID,
                    cancellationToken
                );
            }
        );

    [McpServerTool(Name = "manage_knowledge_entry")]
    public Task<MCPToolResponse> ManageKnowledgeEntryAsync
    (
        long                 projectID,
        ProjectContentAction action,
        KnowledgeEntry?      entry = null,
        long?                entryID = null,
        CancellationToken    cancellationToken = default
    ) =>
        ExecuteAsync
        (
            "manage_knowledge_entry",
            new { projectID, action, entry, entryID },
            async () =>
            {
                await projectEditWindowCoordinator.CloseForExternalChangeAsync(projectID);
                var result = await projectContentService.ManageKnowledgeEntryAsync
                (
                    projectID,
                    action,
                    entry,
                    entryID,
                    cancellationToken
                );
                var indexStatus = action is ProjectContentAction.Create or ProjectContentAction.Update ?
                                      await TryIndexKnowledgeAsync(projectID, cancellationToken) :
                                      "not_required";

                return new { entry = result, indexStatus };
            }
        );

    [McpServerTool(Name = "manage_character_category")]
    public Task<MCPToolResponse> ManageCharacterCategoryAsync
    (
        long                 projectID,
        ProjectContentAction action,
        CharacterCategory?   category = null,
        long?                categoryID = null,
        CancellationToken    cancellationToken = default
    ) =>
        ExecuteAsync
        (
            "manage_character_category",
            new { projectID, action, category, categoryID },
            async () =>
            {
                await projectEditWindowCoordinator.CloseForExternalChangeAsync(projectID);
                return await projectContentService.ManageCharacterCategoryAsync
                (
                    projectID,
                    action,
                    category,
                    categoryID,
                    cancellationToken
                );
            }
        );

    [McpServerTool(Name = "manage_state_attribute")]
    public Task<MCPToolResponse> ManageStateAttributeAsync
    (
        long                      projectID,
        ProjectContentAction      action,
        StateAttributeDefinition? definition = null,
        long?                     attributeID = null,
        CancellationToken         cancellationToken = default
    ) =>
        ExecuteAsync
        (
            "manage_state_attribute",
            new { projectID, action, definition, attributeID },
            async () =>
            {
                await projectEditWindowCoordinator.CloseForExternalChangeAsync(projectID);
                var result = await projectContentService.ManageStateAttributeAsync
                (
                    projectID,
                    action,
                    definition,
                    attributeID,
                    cancellationToken
                );

                if (action == ProjectContentAction.Delete)
                    return new { attributeID = result.ID, deleted = true };

                var snapshot = await projectContentService.GetProjectAsync(projectID, cancellationToken) ??
                               throw new InvalidOperationException($"项目不存在: ID={projectID}");

                return snapshot.StateAttributes.First(attribute => attribute.ID == result.ID);
            }
        );

    private async Task<string> TryIndexKnowledgeAsync(long projectID, CancellationToken cancellationToken)
    {
        var embeddingConfig = agentConfigResolver.ResolveEmbedding(userSettings.EmbeddingConfig);

        if (embeddingConfig is null)
            return "not_configured";

        try
        {
            var entries = await knowledgeRepository.GetPendingIndexEntriesAsync
            (
                projectID,
                embeddingConfig.Fingerprint,
                100,
                cancellationToken
            );
            await embeddingIndexService.IndexKnowledgeAsync(entries, embeddingConfig, cancellationToken);
            return "completed";
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "MCP 知识索引失败: {ProjectID}", projectID);
            return $"failed: {exception.Message}";
        }
    }

    private static string ValidateLocalPath(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("路径不能为空", parameterName);

        var path = Path.GetFullPath(value);

        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("仅支持本机绝对路径", parameterName);

        return path;
    }

    private static async Task<MCPToolResponse> ExecuteAsync
    (
        string              toolName,
        object?             parameters,
        Func<Task<object?>> operation
    )
    {
        Log.Information("内部 MCP 调用: {ToolName}, 参数={@Parameters}", toolName, parameters);

        try
        {
            var data = await operation();
            var response = new MCPToolResponse(1, true, data);
            Log.Information("内部 MCP 返回: {ToolName}, 响应={@Response}", toolName, response);
            return response;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "内部 MCP 调用失败: {ToolName}, 参数={@Parameters}", toolName, parameters);
            var response = new MCPToolResponse(1, false, null, exception.Message);
            Log.Information("内部 MCP 返回: {ToolName}, 响应={@Response}", toolName, response);
            return response;
        }
    }
}
