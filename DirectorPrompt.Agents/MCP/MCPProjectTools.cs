using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Serilog;

namespace DirectorPrompt.Agents.MCP;

public sealed class MCPProjectTools
(
    IProjectContentService projectContentService,
    IProjectPortService    projectPortService
)
{
    [McpServerTool
    (
        Name = "list_projects",
        Title = "列出项目",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("列出项目")]
    public Task<IReadOnlyList<Project>> ListProjectsAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(new { }, () => projectContentService.ListProjectsAsync(cancellationToken));

    [McpServerTool
    (
        Name = "get_project_snapshot",
        Title = "读取项目快照",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("读取项目完整配置")]
    public Task<ProjectContentSnapshot> GetProjectSnapshotAsync
    (
        [Description("项目 ID")] long projectID,
        CancellationToken           cancellationToken = default
    ) =>
        ExecuteAsync(new { projectID }, () => GetRequiredProjectSnapshotAsync(projectID, cancellationToken));

    [McpServerTool
    (
        Name = "create_project",
        Title = "创建项目",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("创建项目；dryRun 时不写入")]
    public Task<ProjectBlueprintResult> CreateProjectAsync
    (
        [Description("项目名称，不能为空")] string            name,
        [Description("项目描述")]      string            description,
        [Description("开场消息")]      string            openingMessage,
        [Description("初始配置蓝图")]    ProjectBlueprint? blueprint         = null,
        [Description("仅校验并预览")]    bool              dryRun            = false,
        CancellationToken                            cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { name, description, openingMessage, blueprint, dryRun },
            () => projectContentService.CreateProjectAsync
            (
                name,
                description,
                openingMessage,
                blueprint,
                dryRun,
                cancellationToken
            )
        );

    [McpServerTool
    (
        Name = "update_project",
        Title = "更新项目",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("局部更新项目基本信息")]
    public Task<Project> UpdateProjectAsync
    (
        [Description("项目 ID")]       long    projectID,
        [Description("项目名称")]        string? name              = null,
        [Description("项目描述；空字符串清空")] string? description       = null,
        [Description("开场消息；空字符串清空")] string? openingMessage    = null,
        CancellationToken                    cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, name, description, openingMessage },
            () => projectContentService.PatchProjectAsync
            (
                projectID,
                new ProjectPatch
                {
                    Name           = name,
                    Description    = description,
                    OpeningMessage = openingMessage
                },
                cancellationToken
            )
        );

    [McpServerTool
    (
        Name = "delete_project",
        Title = "删除项目",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("永久删除项目")]
    public Task<ProjectDeleteSummary> DeleteProjectAsync
    (
        [Description("项目 ID")] long projectID,
        CancellationToken           cancellationToken = default
    ) =>
        ExecuteAsync(new { projectID }, () => projectContentService.DeleteProjectAsync(projectID, cancellationToken));

    [McpServerTool
    (
        Name = "import_project",
        Title = "导入项目",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("从项目包或角色卡导入项目")]
    public Task<ProjectImportResult> ImportProjectAsync
    (
        [Description("导入文件的本机绝对路径")] string sourcePath,
        [Description("导入格式：DirectorPromptPackage 或 SillyTavernCharacterCard")]
        ProjectImportFormat format,
        CancellationToken cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { sourcePath, format },
            async () =>
            {
                var path = ValidateAbsolutePath(sourcePath, nameof(sourcePath));

                if (!File.Exists(path))
                    throw new FileNotFoundException("导入文件不存在", path);

                var result = format switch
                {
                    ProjectImportFormat.DirectorPromptPackage =>
                        await projectPortService.ImportAsync(path, true, cancellationToken),
                    ProjectImportFormat.SillyTavernCharacterCard =>
                        await projectPortService.ImportSillyTavernAsync(path, cancellationToken),
                    _ => throw new ArgumentOutOfRangeException(nameof(format))
                };

                projectContentService.NotifyProjectChanged(result.ProjectID);
                return result;
            }
        );

    [McpServerTool
    (
        Name = "export_project",
        Title = "导出项目",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("导出项目包")]
    public Task<ProjectExportResult> ExportProjectAsync
    (
        [Description("项目 ID")]       long   projectID,
        [Description("目标文件的本机绝对路径")] string destinationPath,
        [Description("覆盖已有文件")]      bool   overwrite         = false,
        CancellationToken                   cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, destinationPath, overwrite },
            async () =>
            {
                var path = ValidateAbsolutePath(destinationPath, nameof(destinationPath));

                if (File.Exists(path) && !overwrite)
                    throw new IOException($"导出目标已存在: {path}");

                var directory = Path.GetDirectoryName(path);

                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    throw new DirectoryNotFoundException($"导出目录不存在: {directory}");

                var temporaryPath = Path.Combine(directory, $".{Path.GetRandomFileName()}.tmp");

                try
                {
                    await projectPortService.ExportAsync(projectID, temporaryPath, cancellationToken);
                    File.Move(temporaryPath, path, overwrite);
                    return new ProjectExportResult(projectID, path);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }
        );

    [McpServerTool
    (
        Name = "create_knowledge_group",
        Title = "创建知识分组",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("创建知识分组")]
    public Task<KnowledgeGroup> CreateKnowledgeGroupAsync
    (
        [Description("项目 ID")]       long    projectID,
        [Description("知识分组名称，不能为空")] string  name,
        [Description("分组描述")]        string? description       = null,
        [Description("是否启用")]        bool    active            = true,
        CancellationToken                    cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, name, description, active },
            () => projectContentService.ManageKnowledgeGroupAsync
            (
                projectID,
                ProjectContentAction.Create,
                new KnowledgeGroup
                {
                    Name        = name,
                    Description = description,
                    Active      = active
                },
                null,
                cancellationToken
            )
        );

    [McpServerTool
    (
        Name = "update_knowledge_group",
        Title = "更新知识分组",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("局部更新知识分组")]
    public Task<KnowledgeGroup> UpdateKnowledgeGroupAsync
    (
        [Description("项目 ID")]       long    projectID,
        [Description("知识分组 ID")]     long    groupID,
        [Description("分组名称")]        string? name              = null,
        [Description("分组描述；空字符串清空")] string? description       = null,
        [Description("是否启用")]        bool?   active            = null,
        CancellationToken                    cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, groupID, name, description, active },
            () => projectContentService.PatchKnowledgeGroupAsync
            (
                projectID,
                groupID,
                new KnowledgeGroupPatch
                {
                    Name        = name,
                    Description = description,
                    Active      = active
                },
                cancellationToken
            )
        );

    [McpServerTool
    (
        Name = "delete_knowledge_group",
        Title = "删除知识分组",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("删除知识分组及其中条目")]
    public Task<KnowledgeGroup> DeleteKnowledgeGroupAsync
    (
        [Description("项目 ID")]   long projectID,
        [Description("知识分组 ID")] long groupID,
        CancellationToken             cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, groupID },
            () => projectContentService.ManageKnowledgeGroupAsync
            (
                projectID,
                ProjectContentAction.Delete,
                null,
                groupID,
                cancellationToken
            )
        );

    [McpServerTool
    (
        Name = "create_knowledge_entry",
        Title = "创建知识条目",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("创建知识条目")]
    public Task<KnowledgeEntry> CreateKnowledgeEntryAsync
    (
        [Description("项目 ID")]   long      projectID,
        [Description("条目备注")]    string    remarks,
        [Description("条目内容")]    string    content,
        [Description("所属分组 ID")] long      groupID,
        [Description("匹配关键词")]   string[]? keywords          = null,
        [Description("是否启用")]    bool      active            = true,
        CancellationToken                  cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, remarks, content, groupID, keywords, active },
            () => projectContentService.ManageKnowledgeEntryAsync
            (
                projectID,
                ProjectContentAction.Create,
                new KnowledgeEntry
                {
                    Remarks  = remarks,
                    Content  = content,
                    Keywords = keywords ?? [],
                    GroupID  = groupID,
                    Active   = active
                },
                null,
                cancellationToken
            )
        );

    [McpServerTool
    (
        Name = "update_knowledge_entry",
        Title = "更新知识条目",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("局部更新知识条目")]
    public Task<KnowledgeEntry> UpdateKnowledgeEntryAsync
    (
        [Description("项目 ID")]       long      projectID,
        [Description("知识条目 ID")]     long      entryID,
        [Description("条目备注")]        string?   remarks           = null,
        [Description("条目内容")]        string?   content           = null,
        [Description("匹配关键词；空数组清空")] string[]? keywords          = null,
        [Description("目标分组 ID")]     long?     groupID           = null,
        [Description("是否启用")]        bool?     active            = null,
        CancellationToken                      cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, entryID, remarks, content, keywords, groupID, active },
            async () =>
            {
                var entry = await GetRequiredKnowledgeEntryAsync(projectID, entryID, cancellationToken);

                if (entry.GroupID is null && groupID is null)
                    throw new InvalidOperationException("未分组知识条目必须提供 groupID 以归属到知识分组");

                return await projectContentService.PatchKnowledgeEntryAsync
                       (
                           projectID,
                           entryID,
                           new KnowledgeEntryPatch
                           {
                               Remarks  = remarks,
                               Content  = content,
                               Keywords = keywords,
                               GroupID  = groupID,
                               Active   = active
                           },
                           cancellationToken
                       );
            }
        );

    [McpServerTool
    (
        Name = "delete_knowledge_entry",
        Title = "删除知识条目",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("删除知识条目")]
    public Task<KnowledgeEntry> DeleteKnowledgeEntryAsync
    (
        [Description("项目 ID")]   long projectID,
        [Description("知识条目 ID")] long entryID,
        CancellationToken             cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, entryID },
            () => projectContentService.ManageKnowledgeEntryAsync
            (
                projectID,
                ProjectContentAction.Delete,
                null,
                entryID,
                cancellationToken
            )
        );

    [McpServerTool
    (
        Name = "create_character_category",
        Title = "创建人物分类",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("创建人物分类")]
    public Task<CharacterCategory> CreateCharacterCategoryAsync
    (
        [Description("项目 ID")] long    projectID,
        [Description("分类名称")]  string  name,
        [Description("分类描述")]  string? description = null,
        [Description("状态属性继承来源的上级分类 ID；空数组取消继承")]
        long[]? parentCategoryIDs = null,
        CancellationToken cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, name, description, parentCategoryIDs },
            () => projectContentService.ManageCharacterCategoryAsync
            (
                projectID,
                ProjectContentAction.Create,
                new CharacterCategory
                {
                    Name              = name,
                    Description       = description,
                    ParentCategoryIDs = parentCategoryIDs ?? []
                },
                null,
                cancellationToken
            )
        );

    [McpServerTool
    (
        Name = "update_character_category",
        Title = "更新人物分类",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("局部更新人物分类")]
    public Task<CharacterCategory> UpdateCharacterCategoryAsync
    (
        [Description("项目 ID")]   long    projectID,
        [Description("人物分类 ID")] long    categoryID,
        [Description("分类名称")]    string? name        = null,
        [Description("分类描述")]    string? description = null,
        [Description("状态属性继承来源的上级分类 ID；空数组取消继承")]
        long[]? parentCategoryIDs = null,
        CancellationToken cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, categoryID, name, description, parentCategoryIDs },
            () => projectContentService.PatchCharacterCategoryAsync
            (
                projectID,
                categoryID,
                new CharacterCategoryPatch
                {
                    Name              = name,
                    Description       = description,
                    ParentCategoryIDs = parentCategoryIDs
                },
                cancellationToken
            )
        );

    [McpServerTool
    (
        Name = "delete_character_category",
        Title = "删除人物分类",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("删除人物分类及关联配置")]
    public Task<CharacterCategory> DeleteCharacterCategoryAsync
    (
        [Description("项目 ID")]   long projectID,
        [Description("人物分类 ID")] long categoryID,
        CancellationToken             cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, categoryID },
            () => projectContentService.ManageCharacterCategoryAsync
            (
                projectID,
                ProjectContentAction.Delete,
                null,
                categoryID,
                cancellationToken
            )
        );

    [McpServerTool
    (
        Name = "list_character_category_state_attributes",
        Title = "列出人物分类状态属性",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("读取人物分类状态属性")]
    public Task<IReadOnlyList<ProjectStateAttribute>> ListCharacterCategoryStateAttributesAsync
    (
        [Description("项目 ID")]   long projectID,
        [Description("人物分类 ID")] long categoryID,
        CancellationToken             cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, categoryID },
            async () =>
            {
                var snapshot = await GetRequiredProjectSnapshotAsync(projectID, cancellationToken);

                if (snapshot.CharacterCategories.All(category => category.ID != categoryID))
                    throw new InvalidOperationException($"人物分类不存在: ID={categoryID}");

                return (IReadOnlyList<ProjectStateAttribute>)snapshot.StateAttributes
                                                                     .Where
                                                                     (attribute => attribute is { Scope: StateScope.Category, CategoryID: not null } &&
                                                                                   attribute.CategoryID == categoryID
                                                                     )
                                                                     .ToList();
            }
        );

    [McpServerTool
    (
        Name = "create_numeric_state_attribute",
        Title = "创建数值状态属性",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("创建数值状态属性")]
    public Task<ProjectStateAttribute> CreateNumericStateAttributeAsync
    (
        [Description("项目 ID")]            long                     projectID,
        [Description("属性标识，供表达式引用")]      string                   name,
        [Description("显示名")]              string                   displayName,
        [Description("所属分类 ID；留空则为全局属性")] long?                    categoryID        = null,
        [Description("驱动方式")]             Driver                   driver            = Driver.Narrative,
        [Description("最小值")]              float?                   min               = null,
        [Description("最大值")]              float?                   max               = null,
        [Description("初始值")]              float?                   initial           = null,
        [Description("单位")]               string?                  unit              = null,
        [Description("变更指引")]             string?                  changeRules       = null,
        [Description("系统驱动时的数值变更条件")]     MCPNumericStateChange[]? changes           = null,
        CancellationToken                                          cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, name, displayName, categoryID, driver, min, max, initial, unit, changeRules, changes },
            () => CreateStateAttributeAsync
            (
                projectID,
                new StateAttributeDefinition
                {
                    Name        = name,
                    DisplayName = displayName,
                    Scope = categoryID is null ?
                                StateScope.Global :
                                StateScope.Category,
                    CategoryID = categoryID,
                    ValueType  = StateValueType.Numeric,
                    Driver     = driver,
                    Numeric = new NumericStateDefinition
                    {
                        Min         = min,
                        Max         = max,
                        Initial     = initial,
                        Unit        = unit,
                        ChangeRules = changeRules,
                        Changes     = changes?.Select(ToNumericStateChange).ToList() ?? []
                    }
                },
                cancellationToken
            )
        );

    [McpServerTool
    (
        Name = "create_enum_state_attribute",
        Title = "创建枚举状态属性",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("创建枚举状态属性")]
    public Task<ProjectStateAttribute> CreateEnumStateAttributeAsync
    (
        [Description("项目 ID")]            long          projectID,
        [Description("属性标识，供表达式引用")]      string        name,
        [Description("显示名")]              string        displayName,
        [Description("枚举选项，至少一项")]        string[]      options,
        [Description("系统驱动时的触发时机")]       SystemTrigger trigger    = SystemTrigger.SceneChange,
        [Description("所属分类 ID；留空则为全局属性")] long?         categoryID = null,
        [Description("驱动方式")]             Driver        driver     = Driver.System,
        [Description("各枚举选项的配置；叙事驱动时填写 option 和 changeRules")]
        MCPEnumStateTransition[]? transitions = null,
        CancellationToken cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, name, displayName, options, trigger, categoryID, driver, transitions },
            async () =>
            {
                EnsureDriver(driver);
                EnsureSystemTrigger(trigger);

                var values = NormalizeEnumOptions(options);
                var scope = categoryID is null ?
                                StateScope.Global :
                                StateScope.Category;
                var enumTransitions = ToEnumTransitions(values, transitions, driver);

                if (driver == Driver.System && transitions is null)
                    enumTransitions = EnsureSystemEnumTransitions(values, enumTransitions);

                var snapshot = await GetRequiredProjectSnapshotAsync(projectID, cancellationToken);

                ValidateEnumTransitionAttributes
                (
                    enumTransitions,
                    snapshot.StateAttributes,
                    name,
                    scope,
                    categoryID,
                    null
                );

                return await CreateStateAttributeAsync
                       (
                           projectID,
                           new StateAttributeDefinition
                           {
                               Name        = name,
                               DisplayName = displayName,
                               Scope       = scope,
                               CategoryID  = categoryID,
                               ValueType   = StateValueType.Enum,
                               Driver      = driver,
                               Enumeration = new EnumStateDefinition
                               {
                                   Options     = values,
                                   Trigger     = trigger,
                                   Transitions = enumTransitions
                               }
                           },
                           cancellationToken
                       );
            }
        );

    [McpServerTool
    (
        Name = "update_state_attribute",
        Title = "更新状态属性",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("局部更新状态属性基本信息")]
    public Task<ProjectStateAttribute> UpdateStateAttributeAsync
    (
        [Description("项目 ID")]         long        projectID,
        [Description("状态属性 ID")]       long        attributeID,
        [Description("属性标识")]          string?     name              = null,
        [Description("显示名")]           string?     displayName       = null,
        [Description("作用域")]           StateScope? scope             = null,
        [Description("分类作用域的所属分类 ID")] long?       categoryID        = null,
        [Description("驱动方式")]          Driver?     driver            = null,
        CancellationToken                          cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, attributeID, name, displayName, scope, categoryID, driver },
            async () =>
            {
                var attribute = await GetProjectStateAttributeAsync(projectID, attributeID, cancellationToken);

                if (scope is null && categoryID is not null)
                    throw new ArgumentException("设置 categoryID 时必须同时将 scope 设为 Category", nameof(categoryID));

                if (scope == StateScope.Category && categoryID is null)
                    throw new ArgumentException("分类状态属性必须提供 categoryID", nameof(categoryID));

                return await PatchStateAttributeAsync
                       (
                           projectID,
                           attributeID,
                           new StateAttributePatch
                           {
                               Name        = name,
                               DisplayName = displayName,
                               Scope       = scope,
                               CategoryID  = categoryID,
                               Driver      = driver
                           },
                           cancellationToken
                       );
            }
        );

    [McpServerTool
    (
        Name = "configure_numeric_state_attribute",
        Title = "配置数值状态属性",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("替换数值状态属性配置")]
    public Task<ProjectStateAttribute> ConfigureNumericStateAttributeAsync
    (
        [Description("项目 ID")]        long                     projectID,
        [Description("数值状态属性 ID")]    long                     attributeID,
        [Description("最小值")]          float?                   min               = null,
        [Description("最大值")]          float?                   max               = null,
        [Description("初始值")]          float?                   initial           = null,
        [Description("单位")]           string?                  unit              = null,
        [Description("变更指引")]         string?                  changeRules       = null,
        [Description("系统驱动时的数值变更条件")] MCPNumericStateChange[]? changes           = null,
        CancellationToken                                      cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, attributeID, min, max, initial, unit, changeRules, changes },
            async () =>
            {
                var attribute = await GetProjectStateAttributeAsync(projectID, attributeID, cancellationToken);
                EnsureStateValueType(attribute, StateValueType.Numeric);

                return await PatchStateAttributeAsync
                       (
                           projectID,
                           attributeID,
                           new StateAttributePatch
                           {
                               Numeric = new NumericStateDefinition
                               {
                                   Min         = min,
                                   Max         = max,
                                   Initial     = initial,
                                   Unit        = unit,
                                   ChangeRules = changeRules,
                                   Changes     = changes?.Select(ToNumericStateChange).ToList() ?? []
                               }
                           },
                           cancellationToken
                       );
            }
        );

    [McpServerTool
    (
        Name = "configure_enum_state_attribute",
        Title = "配置枚举状态属性",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("替换枚举状态属性配置")]
    public Task<ProjectStateAttribute> ConfigureEnumStateAttributeAsync
    (
        [Description("项目 ID")]              long           projectID,
        [Description("枚举状态属性 ID")]          long           attributeID,
        [Description("枚举选项，至少一项")]          string[]       options,
        [Description("系统驱动时的触发时机；留空时保持不变")] SystemTrigger? trigger = null,
        [Description("驱动方式；留空时保持不变")]       Driver?        driver  = null,
        [Description("各枚举选项的配置；叙事驱动时填写 option 和 changeRules")]
        MCPEnumStateTransition[]? transitions = null,
        CancellationToken cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, attributeID, options, trigger, driver, transitions },
            async () =>
            {
                var snapshot  = await GetRequiredProjectSnapshotAsync(projectID, cancellationToken);
                var attribute = GetRequiredProjectStateAttribute(snapshot, attributeID);
                EnsureStateValueType(attribute, StateValueType.Enum);
                var values          = NormalizeEnumOptions(options);
                var resolvedDriver  = driver  ?? attribute.Driver;
                var resolvedTrigger = trigger ?? GetSystemTrigger(attribute);
                var enumTransitions = transitions is null ?
                                          attribute.Configuration.Transitions?
                                                   .Where(transition => values.Contains(transition.Option))
                                                   .ToList() ??
                                          [] :
                                          ToEnumTransitions(values, transitions, resolvedDriver);

                EnsureDriver(resolvedDriver);
                EnsureSystemTrigger(resolvedTrigger);

                if (resolvedDriver == Driver.System && transitions is null)
                    enumTransitions = EnsureSystemEnumTransitions(values, enumTransitions);

                ValidateEnumTransitionAttributes
                (
                    enumTransitions,
                    snapshot.StateAttributes,
                    attribute.Name,
                    attribute.Scope,
                    attribute.CategoryID,
                    attribute.ID
                );

                return await PatchStateAttributeAsync
                       (
                           projectID,
                           attributeID,
                           new StateAttributePatch
                           {
                               Driver = driver,
                               Enumeration = new EnumStateDefinition
                               {
                                   Options     = values,
                                   Trigger     = resolvedTrigger,
                                   Transitions = enumTransitions
                               }
                           },
                           cancellationToken
                       );
            }
        );

    [McpServerTool
    (
        Name = "configure_enum_state_transitions",
        Title = "配置枚举状态转移",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("替换枚举选项配置；叙事驱动时填写 option 和 changeRules")]
    public Task<ProjectStateAttribute> ConfigureEnumStateTransitionsAsync
    (
        [Description("项目 ID")]      long                     projectID,
        [Description("枚举状态属性 ID")]  long                     attributeID,
        [Description("转移规则；空数组清空")] MCPEnumStateTransition[] transitions,
        CancellationToken                                    cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, attributeID, transitions },
            async () =>
            {
                var snapshot  = await GetRequiredProjectSnapshotAsync(projectID, cancellationToken);
                var attribute = GetRequiredProjectStateAttribute(snapshot, attributeID);
                EnsureStateValueType(attribute, StateValueType.Enum);

                var options         = attribute.Configuration.Options ?? [];
                var enumTransitions = ToEnumTransitions(options, transitions, attribute.Driver);

                ValidateEnumTransitionAttributes
                (
                    enumTransitions,
                    snapshot.StateAttributes,
                    attribute.Name,
                    attribute.Scope,
                    attribute.CategoryID,
                    attribute.ID
                );

                return await PatchStateAttributeAsync
                       (
                           projectID,
                           attributeID,
                           new StateAttributePatch
                           {
                               Enumeration = new EnumStateDefinition
                               {
                                   Options     = [.. options],
                                   Trigger     = GetSystemTrigger(attribute),
                                   Transitions = enumTransitions
                               }
                           },
                           cancellationToken
                       );
            }
        );

    [McpServerTool
    (
        Name = "configure_state_attribute_phases",
        Title = "配置状态属性阶段",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("替换状态属性阶段规则")]
    public Task<ProjectStateAttribute> ConfigureStateAttributePhasesAsync
    (
        [Description("项目 ID")]      long            projectID,
        [Description("状态属性 ID")]    long            attributeID,
        [Description("阶段规则；空数组清空")] MCPStatePhase[] phases,
        CancellationToken                           cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, attributeID, phases },
            async () =>
            {
                var snapshot = await GetRequiredProjectSnapshotAsync(projectID, cancellationToken);

                if (snapshot.StateAttributes.All(attribute => attribute.ID != attributeID))
                    throw new InvalidOperationException($"状态属性不存在: ID={attributeID}");

                EnsurePhaseKnowledgeIsDisabled(phases, snapshot);

                return await PatchStateAttributeAsync
                       (
                           projectID,
                           attributeID,
                           new StateAttributePatch { Phases = phases.Select(ToPhaseDefinition).ToList() },
                           cancellationToken
                       );
            }
        );

    [McpServerTool
    (
        Name = "delete_state_attribute",
        Title = "删除状态属性",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("删除状态属性及其引用")]
    public Task<ProjectStateAttribute> DeleteStateAttributeAsync
    (
        [Description("项目 ID")]   long projectID,
        [Description("状态属性 ID")] long attributeID,
        CancellationToken             cancellationToken = default
    ) =>
        ExecuteAsync
        (
            new { projectID, attributeID },
            async () =>
            {
                var attribute = await GetProjectStateAttributeAsync(projectID, attributeID, cancellationToken);

                await projectContentService.ManageStateAttributeAsync
                (
                    projectID,
                    ProjectContentAction.Delete,
                    null,
                    attributeID,
                    cancellationToken
                );

                return attribute;
            }
        );

    private async Task<ProjectStateAttribute> CreateStateAttributeAsync
    (
        long                     projectID,
        StateAttributeDefinition definition,
        CancellationToken        cancellationToken
    )
    {
        var attribute = await projectContentService.ManageStateAttributeAsync
                        (
                            projectID,
                            ProjectContentAction.Create,
                            definition,
                            null,
                            cancellationToken
                        );

        return await GetProjectStateAttributeAsync(projectID, attribute.ID, cancellationToken);
    }

    private async Task<ProjectStateAttribute> PatchStateAttributeAsync
    (
        long                projectID,
        long                attributeID,
        StateAttributePatch patch,
        CancellationToken   cancellationToken
    )
    {
        var attribute = await projectContentService.PatchStateAttributeAsync
                        (
                            projectID,
                            attributeID,
                            patch,
                            cancellationToken
                        );

        return await GetProjectStateAttributeAsync(projectID, attribute.ID, cancellationToken);
    }

    private async Task<ProjectContentSnapshot> GetRequiredProjectSnapshotAsync
    (
        long              projectID,
        CancellationToken cancellationToken
    ) =>
        await projectContentService.GetProjectAsync(projectID, cancellationToken) ??
        throw new InvalidOperationException($"项目不存在: ID={projectID}");

    private async Task<ProjectStateAttribute> GetProjectStateAttributeAsync
    (
        long              projectID,
        long              attributeID,
        CancellationToken cancellationToken
    )
    {
        var snapshot = await GetRequiredProjectSnapshotAsync(projectID, cancellationToken);

        return GetRequiredProjectStateAttribute(snapshot, attributeID);
    }

    private static ProjectStateAttribute GetRequiredProjectStateAttribute
    (
        ProjectContentSnapshot snapshot,
        long                   attributeID
    ) =>
        snapshot.StateAttributes.FirstOrDefault(attribute => attribute.ID == attributeID) ??
        throw new InvalidOperationException($"状态属性不存在: ID={attributeID}");

    private async Task<KnowledgeEntry> GetRequiredKnowledgeEntryAsync
    (
        long              projectID,
        long              entryID,
        CancellationToken cancellationToken
    )
    {
        var snapshot = await GetRequiredProjectSnapshotAsync(projectID, cancellationToken);

        return snapshot.UngroupedKnowledgeEntries
                       .Concat(snapshot.KnowledgeGroups.SelectMany(group => group.Entries))
                       .FirstOrDefault(entry => entry.ID == entryID) ??
               throw new InvalidOperationException($"知识条目不存在: ID={entryID}");
    }

    private static void EnsureStateValueType(ProjectStateAttribute attribute, StateValueType expectedValueType)
    {
        if (attribute.ValueType != expectedValueType)
            throw new ArgumentException($"状态属性 {attribute.ID} 不是 {expectedValueType} 类型");
    }

    private static List<string> NormalizeEnumOptions(IEnumerable<string>? options)
    {
        if (options is null)
            throw new ArgumentException("枚举选项不能为空", nameof(options));

        var values = options.Where(option => !string.IsNullOrWhiteSpace(option))
                            .Select(option => option.Trim())
                            .Distinct(StringComparer.Ordinal)
                            .ToList();

        if (values.Count == 0)
            throw new ArgumentException("枚举选项至少包含一项", nameof(options));

        return values;
    }

    private static void EnsureDriver(Driver driver)
    {
        if (!Enum.IsDefined(driver))
            throw new ArgumentOutOfRangeException(nameof(driver));
    }

    private static void EnsureSystemTrigger(SystemTrigger trigger)
    {
        if (!Enum.IsDefined(trigger))
            throw new ArgumentOutOfRangeException(nameof(trigger));
    }

    private static SystemTrigger GetSystemTrigger(ProjectStateAttribute attribute) =>
        Enum.TryParse<SystemTrigger>(attribute.Configuration.Trigger, true, out var trigger) && Enum.IsDefined(trigger) ?
            trigger :
            SystemTrigger.SceneChange;

    private static List<EnumTransitionConfig> ToEnumTransitions
    (
        IReadOnlyCollection<string>          options,
        IEnumerable<MCPEnumStateTransition>? transitions,
        Driver                               driver
    )
    {
        EnsureDriver(driver);

        if (transitions is null)
            return [];

        var optionSet = options.ToHashSet(StringComparer.Ordinal);
        var values    = transitions.Select(transition => ToEnumTransition(transition, driver)).ToList();

        if (values.Any(transition => !optionSet.Contains(transition.Option)))
            throw new ArgumentException("转移规则引用了不存在的枚举选项", nameof(transitions));

        if (values.Select(transition => transition.Option).Distinct(StringComparer.Ordinal).Count() != values.Count)
            throw new ArgumentException("每个枚举选项只能配置一条规则", nameof(transitions));

        return values;
    }

    private static List<EnumTransitionConfig> EnsureSystemEnumTransitions
    (
        IReadOnlyCollection<string>         options,
        IReadOnlyList<EnumTransitionConfig> transitions
    )
    {
        var values            = transitions.ToList();
        var configuredOptions = values.Select(transition => transition.Option).ToHashSet(StringComparer.Ordinal);

        foreach (var option in options)
        {
            if (!configuredOptions.Add(option))
                continue;

            values.Add(new EnumTransitionConfig { Option = option });
        }

        return values;
    }

    private static void ValidateEnumTransitionAttributes
    (
        IEnumerable<EnumTransitionConfig>    transitions,
        IReadOnlyList<ProjectStateAttribute> attributes,
        string                               attributeName,
        StateScope                           scope,
        long?                                categoryID,
        long?                                attributeID
    )
    {
        foreach (var transition in transitions.Where(transition => transition.Method == EnumTransitionMethod.Expression))
        {
            var referencedAttribute = attributes.FirstOrDefault
            (candidate =>
                 candidate.ID         != attributeID              &&
                 candidate.Name       == transition.AttributeName &&
                 candidate.Name       != attributeName            &&
                 candidate.ValueType  == StateValueType.Numeric   &&
                 candidate.Scope      == scope                    &&
                 candidate.CategoryID == categoryID
            );

            if (referencedAttribute is null)
            {
                throw new ArgumentException
                (
                    $"表达式转移关联属性 {transition.AttributeName} 不存在或不属于同一作用域",
                    nameof(transitions)
                );
            }
        }
    }

    private static EnumTransitionConfig ToEnumTransition(MCPEnumStateTransition transition, Driver driver)
    {
        ArgumentNullException.ThrowIfNull(transition);

        if (string.IsNullOrWhiteSpace(transition.Option))
            throw new ArgumentException("枚举转移规则必须指定 option", nameof(transition));

        var option = transition.Option.Trim();

        if (driver == Driver.Narrative)
        {
            return new EnumTransitionConfig
            {
                Option = option,
                ChangeRules = string.IsNullOrWhiteSpace(transition.ChangeRules) ?
                                  null :
                                  transition.ChangeRules
            };
        }

        if (!Enum.IsDefined(transition.Method) || !Enum.IsDefined(transition.SwitchMode))
            throw new ArgumentOutOfRangeException(nameof(transition));

        if (!float.IsFinite(transition.Weight) || transition.Weight < 0)
            throw new ArgumentException("枚举转移规则的 weight 必须为大于或等于 0 的有限数", nameof(transition));

        if (transition.Method == EnumTransitionMethod.Expression &&
            (string.IsNullOrWhiteSpace(transition.AttributeName) || string.IsNullOrWhiteSpace(transition.Expression)))
            throw new ArgumentException("表达式枚举转移规则必须指定 attributeName 和 expression", nameof(transition));

        return new EnumTransitionConfig
        {
            Option        = option,
            Method        = transition.Method,
            Weight        = transition.Weight,
            AttributeName = transition.AttributeName?.Trim(),
            Expression    = transition.Expression?.Trim(),
            SwitchMode    = transition.SwitchMode
        };
    }

    private static NumericStateChangeRuleConfig ToNumericStateChange(MCPNumericStateChange change)
    {
        if (string.IsNullOrWhiteSpace(change.Expression) || string.IsNullOrWhiteSpace(change.ChangeExpression))
            throw new ArgumentException("数值变更条件必须指定 expression 和 changeExpression", nameof(change));

        return new NumericStateChangeRuleConfig
        {
            ID               = Guid.NewGuid().ToString("N"),
            Remarks          = change.Remarks,
            AttributeName    = change.AttributeName,
            Expression       = change.Expression,
            ChangeExpression = change.ChangeExpression,
            Trigger          = change.Trigger,
            SwitchMode       = change.SwitchMode
        };
    }

    private static PhaseDefinition ToPhaseDefinition(MCPStatePhase phase)
    {
        if (string.IsNullOrWhiteSpace(phase.Name))
            throw new ArgumentException("状态阶段名称不能为空", nameof(phase));

        return new PhaseDefinition
        {
            Name              = phase.Name,
            Expression        = phase.Expression,
            KnowledgeEntryIDs = [.. phase.KnowledgeEntryIDs],
            KnowledgeGroupIDs = [.. phase.KnowledgeGroupIDs],
            EnterDirectives   = [.. phase.EnterDirectives],
            ExitDirectives    = [.. phase.ExitDirectives]
        };
    }

    private static void EnsurePhaseKnowledgeIsDisabled
    (
        IEnumerable<MCPStatePhase> phases,
        ProjectContentSnapshot     snapshot
    )
    {
        var activeEntryIDs = snapshot.UngroupedKnowledgeEntries
                                     .Concat(snapshot.KnowledgeGroups.SelectMany(group => group.Entries))
                                     .Where(entry => entry.Active)
                                     .Select(entry => entry.ID)
                                     .ToHashSet();
        var activeGroupIDs = snapshot.KnowledgeGroups
                                     .Where(group => group.Group.Active)
                                     .Select(group => group.Group.ID)
                                     .ToHashSet();

        foreach (var phase in phases)
        {
            if (phase.KnowledgeEntryIDs.Any(activeEntryIDs.Contains))
                throw new ArgumentException($"阶段 {phase.Name} 只能关联已禁用的知识条目", nameof(phases));

            if (phase.KnowledgeGroupIDs.Any(activeGroupIDs.Contains))
                throw new ArgumentException($"阶段 {phase.Name} 只能关联已禁用的知识分组", nameof(phases));
        }
    }

    private static string ValidateAbsolutePath(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("路径不能为空", parameterName);

        if (!Path.IsPathFullyQualified(value))
            throw new ArgumentException("仅支持本机绝对路径", parameterName);

        return Path.GetFullPath(value);
    }

    private static async Task<T> ExecuteAsync<T>
    (
        object                    arguments,
        Func<Task<T>>             operation,
        [CallerMemberName] string toolName = ""
    )
    {
        var startTimestamp = Stopwatch.GetTimestamp();

        Log.Information("内部 MCP 工具调用: {ToolName}, 参数={@Arguments}", toolName, arguments);

        try
        {
            var result = await operation();

            Log.Information
            (
                "内部 MCP 工具返回: {ToolName}, 耗时={ElapsedMilliseconds}ms, 返回={@Result}",
                toolName,
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                result
            );

            return result;
        }
        catch (OperationCanceledException)
        {
            Log.Information
            (
                "内部 MCP 工具调用已取消: {ToolName}, 耗时={ElapsedMilliseconds}ms",
                toolName,
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            );
            throw;
        }
        catch (Exception exception)
        {
            Log.Warning
            (
                exception,
                "内部 MCP 工具调用失败: {ToolName}, 耗时={ElapsedMilliseconds}ms",
                toolName,
                Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds
            );

            if (exception is ArgumentException or
                InvalidOperationException or
                InvalidDataException or
                IOException or
                UnauthorizedAccessException)
                throw new McpException(exception.Message, exception);

            throw;
        }
    }
}
