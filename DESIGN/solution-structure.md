# 解决方案结构

## 项目划分

```
DirectorPrompt/
├── DirectorPrompt.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── DESIGN/
│
├── DirectorPrompt.Domain/            # 领域模型 + 抽象接口
├── DirectorPrompt.Agents/            # Agent 定义 + 编排器 + 工具
├── DirectorPrompt.Infrastructure/    # 数据访问 + 外部集成
└── DirectorPrompt/                   # WPF UI 主程序
```

## 各项目职责

### DirectorPrompt.Domain

纯领域模型和抽象接口, 零外部依赖 (除 System.Text.Json)。

**内容:**
- 实体模型: `Scene`、`Round`、`StateAttribute`、`CompositeItem`、`Flag`、`KnowledgeEntry`、`KnowledgeGroup`、`MemoryEntry`、`Character`、`CharacterCategory`、`CharacterRelation`、`PlaythroughEvent`
- 值对象和枚举: `ValueType`、`Driver`、`SceneStatus`、`CharacterStatus`、`DirectiveType`、`AuditSeverity` 等
- 配置模型: `AuditConfig`、`MemoryConfig`、`KnowledgeRetrievalConfig`、`OrchestratorConfig`
- Effect 模型: `Effect`、`EffectType`
- 事件模型: `PlaythroughEvent`、`EventType`
- 条件表达式引擎: 条件解析和求值 (纯逻辑, 不依赖外部)
- 时间线坐标计算: `timelinePosition` 的中点法、步长计算 (纯数学)
- 仓储接口: `ISceneRepository`、`IStateRepository`、`IKnowledgeRepository`、`IMemoryRepository`、`ICharacterRepository`、`IEventRepository`、`IStateSnapshotRepository`
- 服务接口: `IConditionEngine`、`ITimelineCalculator`、`IEmbeddingService`

**不包含:** AI 相关、数据库相关、UI 相关

### DirectorPrompt.Agents

Agent 定义、编排器、工具实现。依赖 Domain + Microsoft.Extensions.AI。

**内容:**
- Agent 定义: `AgentDefinition`、`ModelConfig`、各 Agent 的 System Prompt 模板
- Orchestrator: 确定性编排器, 流水线调度, 事件记录协调
- 工具实现: 所有 `ChatTool` 的定义和 handler — `create_scene`、`query_knowledge`、`update_state`、`add_character`、`set_relation`、`add_violation` 等。工具 handler 调用 Domain 的仓储接口完成操作
- 流水线阶段: 检索阶段、生成阶段、审计阶段、后处理阶段的编排逻辑
- 指令处理: 指令批次的解析和组装
- 输出操作: 删除、修正、重写的事件回滚逻辑
- 场景管理: 场景创建流程 (调用 query_scene + create_scene tool)

**不包含:** 具体 AI Provider 配置、数据库实现、UI

### DirectorPrompt.Infrastructure

数据访问和外部集成的具体实现。依赖 Domain + Dapper + Microsoft.Data.Sqlite + sqlite-vec + AI Provider 包 + MCP SDK + Polly。

**内容:**
- SQLite 仓储实现: 所有 `I*Repository` 的 Dapper 实现
- 向量检索: sqlite-vec 扩展加载、向量 CRUD、近邻查询封装
- Schema 版本化: `schema_version` 表 + 嵌入式 SQL 脚本 + 迁移执行器
- AI Provider 设置: `IChatClient` 和 `IEmbeddingGenerator` 的工厂/注册, OpenAI/Anthropic/Ollama 的连接配置
- Embedding 服务实现: `IEmbeddingService` 的实现, 调用 `IEmbeddingGenerator` 生成向量
- MCP 集成: MCP Server (对外暴露工具) + MCP Client (连接外部 MCP Server)
- 弹性处理: Polly 策略配置 (重试、熔断), 包装 AI 调用
- 日志配置: Serilog 初始化和 sink 配置

**不包含:** 业务逻辑 (业务规则在 Agents 层)、UI

### DirectorPrompt

WPF 桌面应用主程序。依赖 Domain + Agents + Infrastructure + WPF-UI + CommunityToolkit.Mvvm。

**内容:**
- 启动: `App.xaml`/`App.xaml.cs`, DI 容器注册, Host 构建
- Views: XAML 视图 — 对话区、侧边栏 (状态栏、生效指令、人物列表)、指令组合器、项目设置、知识编辑器、人物编辑器、时间线视图
- ViewModels: MVVM ViewModel, 使用 CommunityToolkit.Mvvm 的 Source Generator
- Markdown 渲染: Markdig → FlowDocument 自定义渲染器
- 转换器: 值转换器、多值转换器
- 资源: 样式、主题、图标
- 配置加载: `appsettings.json` 读取, 项目配置管理

## 依赖关系

```
                    DirectorPrompt.Domain
                   (net10.0, 零外部依赖)
                    /              \
                   ↓                ↓
    DirectorPrompt.Agents     DirectorPrompt.Infrastructure
      (net10.0)                   (net10.0)
   依赖: Domain                依赖: Domain
         M.E.AI                      Dapper
                                      Microsoft.Data.Sqlite
                                      sqlite-vec
                                      AI Provider 包
                                      MCP SDK
                                      Polly
                   \              /
                    ↓            ↓
                      DirectorPrompt
                   (net10.0-windows)
              依赖: Domain + Agents + Infrastructure
                    WPF-UI
                    CommunityToolkit.Mvvm
                    Markdig
                    M.E.Hosting
                    Serilog
```

### 关键依赖规则

- Domain 不依赖任何其他项目
- Agents 只依赖 Domain (不依赖 Infrastructure, 通过接口访问存储)
- Infrastructure 只依赖 Domain (实现 Domain 的接口)
- DirectorPrompt 依赖全部 (组装层)
- Agents 和 Infrastructure 互不依赖 — 它们只在 DirectorPrompt 的 DI 容器中通过接口连接

## Target Framework

| 项目 | TFM | 理由 |
|------|-----|------|
| Domain | `net10.0` | 纯 C#, 无 Windows 依赖, 可跨平台复用 |
| Agents | `net10.0` | M.E.AI 跨平台, Agent 逻辑不依赖 Windows |
| Infrastructure | `net10.0` | SQLite/Dapper/MCP 跨平台 |
| DirectorPrompt | `net10.0-windows` | WPF 需要 Windows |

核心三层 (Domain/Agents/Infrastructure) 都是 `net10.0`, 不绑定 Windows。未来如果要做 CLI 版本或 Web 版本, 核心逻辑可以直接复用, 只需替换 UI 层。

## 设计文档到项目的映射

| 设计文档 | 主要落地项目 |
|---------|------------|
| 核心交互范式 | Domain (模型) + Agents (指令处理) + DirectorPrompt (UI) |
| 时间线系统 | Domain (模型+坐标计算) + Agents (场景工具) + Infrastructure (仓储) |
| 状态系统 | Domain (模型+条件引擎) + Agents (状态工具) + Infrastructure (仓储) |
| 知识系统 | Domain (模型) + Agents (检索工具) + Infrastructure (仓储+向量) |
| 记忆系统 | Domain (模型) + Agents (Memory Sub-Agent) + Infrastructure (仓储+向量) |
| 人物系统 | Domain (模型+分类解析) + Agents (人物工具) + Infrastructure (仓储) |
| 审计系统 | Domain (模型+配置) + Agents (Audit Agent+维度并行) |
| 多 Agent 编排 | Domain (事件模型) + Agents (Orchestrator+流水线) + Infrastructure (事件存储) |
| 技术栈 | Infrastructure (大部分包) + DirectorPrompt (UI 包) |
