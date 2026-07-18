using System.ComponentModel;
using DirectorPrompt.Domain.Configurations;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

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
    [Description("列出所有项目的基础信息，不包含知识、人物分类或状态属性的详情")]
    public Task<IReadOnlyList<Project>> ListProjectsAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => projectContentService.ListProjectsAsync(cancellationToken));

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
    [Description("读取项目的完整可编辑定义，包括项目信息、知识分组与条目、人物分类和状态属性")]
    public Task<ProjectContentSnapshot> GetProjectSnapshotAsync
    (
        [Description("项目 ID")] long projectID,
        CancellationToken           cancellationToken = default
    ) =>
        ExecuteAsync(() => GetRequiredProjectSnapshotAsync(projectID, cancellationToken));

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
    [Description("创建项目，可在同一请求中提供知识分组、人物分类和状态属性蓝图。dryRun 为 true 时仅验证并返回预览，不写入项目")]
    public Task<ProjectBlueprintResult> CreateProjectAsync
    (
        [Description("项目名称，不能为空")]    string            name,
        [Description("项目描述")]         string            description,
        [Description("项目开场消息")]       string            openingMessage,
        [Description("可选的项目初始蓝图")]    ProjectBlueprint? blueprint         = null,
        [Description("是否只校验并预览创建结果")] bool              dryRun            = false,
        CancellationToken                               cancellationToken = default
    ) =>
        ExecuteAsync
        (() => projectContentService.CreateProjectAsync
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
    [Description("局部更新项目基础信息。至少提供 name、description、openingMessage 之一；传入空字符串可清空描述或开场消息")]
    public Task<Project> UpdateProjectAsync
    (
        [Description("项目 ID")]    long    projectID,
        [Description("新的项目名称")]   string? name              = null,
        [Description("新的项目描述")]   string? description       = null,
        [Description("新的项目开场消息")] string? openingMessage    = null,
        CancellationToken                 cancellationToken = default
    ) =>
        ExecuteAsync
        (() => projectContentService.PatchProjectAsync
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
    [Description("永久删除项目及其全部知识、人物分类、状态属性和相关配置，返回删除汇总")]
    public Task<ProjectDeleteSummary> DeleteProjectAsync
    (
        [Description("项目 ID")] long projectID,
        CancellationToken           cancellationToken = default
    ) =>
        ExecuteAsync(() => projectContentService.DeleteProjectAsync(projectID, cancellationToken));

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
    [Description("从本机绝对路径导入 DirectorPrompt 项目包或 SillyTavern 角色卡，并创建新项目")]
    public Task<ProjectImportResult> ImportProjectAsync
    (
        [Description("待导入文件的本机绝对路径")] string sourcePath,
        [Description("导入格式：DirectorPromptPackage 或 SillyTavernCharacterCard")]
        ProjectImportFormat format,
        CancellationToken cancellationToken = default
    ) =>
        ExecuteAsync
        (async () =>
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
    [Description("将项目导出为 DirectorPrompt 项目包。仅接受本机绝对路径；overwrite 为 false 时不会覆盖已有文件")]
    public Task<ProjectExportResult> ExportProjectAsync
    (
        [Description("项目 ID")]       long   projectID,
        [Description("导出文件的本机绝对路径")] string destinationPath,
        [Description("是否覆盖已有导出文件")]  bool   overwrite         = false,
        CancellationToken                   cancellationToken = default
    ) =>
        ExecuteAsync
        (async () =>
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
    [Description("在项目中创建知识分组")]
    public Task<KnowledgeGroup> CreateKnowledgeGroupAsync
    (
        [Description("项目 ID")]       long    projectID,
        [Description("知识分组名称，不能为空")] string  name,
        [Description("知识分组描述")]      string? description       = null,
        [Description("是否启用该知识分组")]   bool    active            = true,
        CancellationToken                    cancellationToken = default
    ) =>
        ExecuteAsync
        (() => projectContentService.ManageKnowledgeGroupAsync
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
    [Description("局部更新知识分组。未提供的字段保持不变，传入空字符串可清空描述")]
    public Task<KnowledgeGroup> UpdateKnowledgeGroupAsync
    (
        [Description("项目 ID")]     long    projectID,
        [Description("知识分组 ID")]   long    groupID,
        [Description("新的知识分组名称")]  string? name              = null,
        [Description("新的知识分组描述")]  string? description       = null,
        [Description("是否启用该知识分组")] bool?   active            = null,
        CancellationToken                  cancellationToken = default
    ) =>
        ExecuteAsync
        (() => projectContentService.PatchKnowledgeGroupAsync
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
    [Description("删除知识分组及其包含的全部知识条目，并清理状态属性中的相关知识引用")]
    public Task<KnowledgeGroup> DeleteKnowledgeGroupAsync
    (
        [Description("项目 ID")]   long projectID,
        [Description("知识分组 ID")] long groupID,
        CancellationToken             cancellationToken = default
    ) =>
        ExecuteAsync
        (() => projectContentService.ManageKnowledgeGroupAsync
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
    [Description("在项目的指定知识分组中创建知识条目。知识条目必须归属于知识分组")]
    public Task<KnowledgeEntry> CreateKnowledgeEntryAsync
    (
        [Description("项目 ID")]      long      projectID,
        [Description("知识条目备注或标题")]  string    remarks,
        [Description("知识条目正文")]     string    content,
        [Description("所属知识分组 ID")]  long      groupID,
        [Description("用于匹配的关键词列表")] string[]? keywords          = null,
        [Description("是否启用该知识条目")]  bool      active            = true,
        CancellationToken                     cancellationToken = default
    ) =>
        ExecuteAsync
        (() => projectContentService.ManageKnowledgeEntryAsync
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
    [Description("局部更新知识条目。groupID 可将条目移动到另一个知识分组；MCP 不允许知识条目无分组")]
    public Task<KnowledgeEntry> UpdateKnowledgeEntryAsync
    (
        [Description("项目 ID")]                long      projectID,
        [Description("知识条目 ID")]              long      entryID,
        [Description("新的备注或标题")]              string?   remarks           = null,
        [Description("新的正文")]                 string?   content           = null,
        [Description("新的关键词列表；传入空数组可清空关键词")]  string[]? keywords          = null,
        [Description("目标知识分组 ID；未提供时保留当前分组")] long?     groupID           = null,
        [Description("是否启用该知识条目")]            bool?     active            = null,
        CancellationToken                               cancellationToken = default
    ) =>
        ExecuteAsync
        (async () =>
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
    [Description("删除知识条目，并清理状态属性中的相关知识引用")]
    public Task<KnowledgeEntry> DeleteKnowledgeEntryAsync
    (
        [Description("项目 ID")]   long projectID,
        [Description("知识条目 ID")] long entryID,
        CancellationToken             cancellationToken = default
    ) =>
        ExecuteAsync
        (() => projectContentService.ManageKnowledgeEntryAsync
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
    [Description("在项目中创建人物分类。parentCategoryIDs 中的分类必须属于同一项目")]
    public Task<CharacterCategory> CreateCharacterCategoryAsync
    (
        [Description("项目 ID")]     long    projectID,
        [Description("人物分类名称")]    string  name,
        [Description("人物分类描述")]    string? description       = null,
        [Description("父分类 ID 列表")] long[]? parentCategoryIDs = null,
        CancellationToken                  cancellationToken = default
    ) =>
        ExecuteAsync
        (() => projectContentService.ManageCharacterCategoryAsync
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
    [Description("局部更新人物分类。parentCategoryIDs 传入空数组可移除全部父分类")]
    public Task<CharacterCategory> UpdateCharacterCategoryAsync
    (
        [Description("项目 ID")]       long    projectID,
        [Description("人物分类 ID")]     long    categoryID,
        [Description("新的分类名称")]      string? name              = null,
        [Description("新的分类描述")]      string? description       = null,
        [Description("新的父分类 ID 列表")] long[]? parentCategoryIDs = null,
        CancellationToken                    cancellationToken = default
    ) =>
        ExecuteAsync
        (() => projectContentService.PatchCharacterCategoryAsync
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
    [Description("删除人物分类，并移除人物、分类状态属性和其他分类对它的引用")]
    public Task<CharacterCategory> DeleteCharacterCategoryAsync
    (
        [Description("项目 ID")]   long projectID,
        [Description("人物分类 ID")] long categoryID,
        CancellationToken             cancellationToken = default
    ) =>
        ExecuteAsync
        (() => projectContentService.ManageCharacterCategoryAsync
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
    [Description("列出指定人物分类拥有的全部状态属性及其完整配置")]
    public Task<IReadOnlyList<ProjectStateAttribute>> ListCharacterCategoryStateAttributesAsync
    (
        [Description("项目 ID")]   long projectID,
        [Description("人物分类 ID")] long categoryID,
        CancellationToken             cancellationToken = default
    ) =>
        ExecuteAsync
        (async () =>
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
    [Description("创建数值状态属性。categoryID 留空时创建全局属性，提供人物分类 ID 时创建该分类专属属性")]
    public Task<ProjectStateAttribute> CreateNumericStateAttributeAsync
    (
        [Description("项目 ID")]              long   projectID,
        [Description("属性内部名称，供表达式和转移规则引用")] string name,
        [Description("属性显示名称")]             string displayName,
        [Description("所属人物分类 ID；留空时为全局属性")] long?  categoryID = null,
        [Description("数值属性驱动方式：Narrative 或 System")]
        Driver driver = Driver.Narrative,
        [Description("最小值")]    float?  min               = null,
        [Description("最大值")]    float?  max               = null,
        [Description("数值单位")]   string? unit              = null,
        [Description("数值变化规则")] string? changeRules       = null,
        CancellationToken               cancellationToken = default
    ) =>
        ExecuteAsync
        (() => CreateStateAttributeAsync
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
                     Unit        = unit,
                     ChangeRules = changeRules
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
    [Description("创建枚举状态属性。枚举属性始终由系统驱动；categoryID 留空时为全局属性")]
    public Task<ProjectStateAttribute> CreateEnumStateAttributeAsync
    (
        [Description("项目 ID")]              long          projectID,
        [Description("属性内部名称，供表达式和转移规则引用")] string        name,
        [Description("属性显示名称")]             string        displayName,
        [Description("可选值列表，至少包含一项")]       string[]      options,
        [Description("枚举变化的触发时机")]          SystemTrigger trigger,
        [Description("所属人物分类 ID；留空时为全局属性")] long?         categoryID        = null,
        CancellationToken                                 cancellationToken = default
    ) =>
        ExecuteAsync
        (() => CreateStateAttributeAsync
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
                 ValueType  = StateValueType.Enum,
                 Enumeration = new EnumStateDefinition
                 {
                     Options = NormalizeEnumOptions(options),
                     Trigger = trigger
                 }
             },
             cancellationToken
         )
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
    [Description("更新状态属性的基础信息。值类型不能在更新时变更；数值规则、枚举选项、转移和阶段使用各自的配置工具")]
    public Task<ProjectStateAttribute> UpdateStateAttributeAsync
    (
        [Description("项目 ID")]    long        projectID,
        [Description("状态属性 ID")]  long        attributeID,
        [Description("新的属性内部名称")] string?     name        = null,
        [Description("新的属性显示名称")] string?     displayName = null,
        [Description("新的作用域")]    StateScope? scope       = null,
        [Description("作用域为 Category 时的所属人物分类 ID")]
        long? categoryID = null,
        [Description("数值属性的新驱动方式；枚举属性不支持设置")] Driver? numericDriver     = null,
        CancellationToken                             cancellationToken = default
    ) =>
        ExecuteAsync
        (async () =>
            {
                var attribute = await GetProjectStateAttributeAsync(projectID, attributeID, cancellationToken);

                if (scope is null && categoryID is not null)
                    throw new ArgumentException("设置 categoryID 时必须同时将 scope 设为 Category", nameof(categoryID));

                if (scope == StateScope.Category && categoryID is null)
                    throw new ArgumentException("分类状态属性必须提供 categoryID", nameof(categoryID));

                if (numericDriver is not null && attribute.ValueType != StateValueType.Numeric)
                    throw new ArgumentException("枚举状态属性始终由系统驱动", nameof(numericDriver));

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
                               Driver      = numericDriver
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
    [Description("完整替换数值状态属性的数值规则。min、max、unit 和 changeRules 均可留空")]
    public Task<ProjectStateAttribute> ConfigureNumericStateAttributeAsync
    (
        [Description("项目 ID")]     long    projectID,
        [Description("数值状态属性 ID")] long    attributeID,
        [Description("最小值")]       float?  min               = null,
        [Description("最大值")]       float?  max               = null,
        [Description("数值单位")]      string? unit              = null,
        [Description("数值变化规则")]    string? changeRules       = null,
        CancellationToken                  cancellationToken = default
    ) =>
        ExecuteAsync
        (async () =>
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
                                   Unit        = unit,
                                   ChangeRules = changeRules
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
    [Description("完整替换枚举状态属性的选项和触发时机。不再存在的选项会从转移规则中移除")]
    public Task<ProjectStateAttribute> ConfigureEnumStateAttributeAsync
    (
        [Description("项目 ID")]        long          projectID,
        [Description("枚举状态属性 ID")]    long          attributeID,
        [Description("可选值列表，至少包含一项")] string[]      options,
        [Description("枚举变化的触发时机")]    SystemTrigger trigger,
        CancellationToken                           cancellationToken = default
    ) =>
        ExecuteAsync
        (async () =>
            {
                var attribute = await GetProjectStateAttributeAsync(projectID, attributeID, cancellationToken);
                EnsureStateValueType(attribute, StateValueType.Enum);
                var values = NormalizeEnumOptions(options);

                return await PatchStateAttributeAsync
                       (
                           projectID,
                           attributeID,
                           new StateAttributePatch
                           {
                               Enumeration = new EnumStateDefinition
                               {
                                   Options = values,
                                   Trigger = trigger,
                                   Transitions = attribute.Configuration.Transitions?
                                                          .Where(transition => values.Contains(transition.Option))
                                                          .ToList() ??
                                                 []
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
    [Description("完整替换枚举状态属性的转移规则。每条规则的 option 必须属于该属性当前的选项列表")]
    public Task<ProjectStateAttribute> ConfigureEnumStateTransitionsAsync
    (
        [Description("项目 ID")]                 long                     projectID,
        [Description("枚举状态属性 ID")]             long                     attributeID,
        [Description("枚举状态转移规则列表；传入空数组可清空规则")] MCPEnumStateTransition[] transitions,
        CancellationToken                                               cancellationToken = default
    ) =>
        ExecuteAsync
        (async () =>
            {
                var attribute = await GetProjectStateAttributeAsync(projectID, attributeID, cancellationToken);
                EnsureStateValueType(attribute, StateValueType.Enum);
                var options = attribute.Configuration.Options ?? [];

                if (transitions.Any(transition => !options.Contains(transition.Option)))
                    throw new ArgumentException("转移规则引用了不存在的枚举选项", nameof(transitions));

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
                                   Transitions = transitions.Select(ToEnumTransition).ToList()
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
    [Description("完整替换状态属性的阶段规则。每个阶段可引用同项目的知识条目、知识分组及进入或退出指令")]
    public Task<ProjectStateAttribute> ConfigureStateAttributePhasesAsync
    (
        [Description("项目 ID")]             long            projectID,
        [Description("状态属性 ID")]           long            attributeID,
        [Description("阶段规则列表；传入空数组可清空阶段")] MCPStatePhase[] phases,
        CancellationToken                                  cancellationToken = default
    ) =>
        ExecuteAsync
        (() => PatchStateAttributeAsync
         (
             projectID,
             attributeID,
             new StateAttributePatch { Phases = phases.Select(ToPhaseDefinition).ToList() },
             cancellationToken
         )
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
    [Description("删除状态属性，并清理其他状态属性转换规则中的相关引用")]
    public Task<ProjectStateAttribute> DeleteStateAttributeAsync
    (
        [Description("项目 ID")]   long projectID,
        [Description("状态属性 ID")] long attributeID,
        CancellationToken             cancellationToken = default
    ) =>
        ExecuteAsync
        (async () =>
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

        return snapshot.StateAttributes.FirstOrDefault(attribute => attribute.ID == attributeID) ??
               throw new InvalidOperationException($"状态属性不存在: ID={attributeID}");
    }

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

    private static SystemTrigger GetSystemTrigger(ProjectStateAttribute attribute) =>
        Enum.TryParse<SystemTrigger>(attribute.Configuration.Trigger, true, out var trigger) ?
            trigger :
            SystemTrigger.SceneChange;

    private static EnumTransitionConfig ToEnumTransition(MCPEnumStateTransition transition)
    {
        if (string.IsNullOrWhiteSpace(transition.Option))
            throw new ArgumentException("枚举转移规则必须指定 option", nameof(transition));

        if (transition.Method == EnumTransitionMethod.Random && transition.Weight <= 0)
            throw new ArgumentException("随机枚举转移规则的 weight 必须大于 0", nameof(transition));

        if (transition.Method == EnumTransitionMethod.Expression &&
            (string.IsNullOrWhiteSpace(transition.AttributeName) || string.IsNullOrWhiteSpace(transition.Expression)))
            throw new ArgumentException("表达式枚举转移规则必须指定 attributeName 和 expression", nameof(transition));

        return new EnumTransitionConfig
        {
            Option        = transition.Option,
            Method        = transition.Method,
            Weight        = transition.Weight,
            AttributeName = transition.AttributeName,
            Expression    = transition.Expression,
            SwitchMode    = transition.SwitchMode
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

    private static string ValidateAbsolutePath(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("路径不能为空", parameterName);

        if (!Path.IsPathFullyQualified(value))
            throw new ArgumentException("仅支持本机绝对路径", parameterName);

        return Path.GetFullPath(value);
    }

    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception) when
        (
            exception is ArgumentException or
            InvalidOperationException or
            InvalidDataException or
            IOException or
                UnauthorizedAccessException
        )
        {
            throw new McpException(exception.Message, exception);
        }
    }
}
