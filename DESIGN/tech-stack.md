# 技术栈

## 确定选型

| 维度 | 选型 | NuGet 包 | 说明 |
|------|------|----------|------|
| 运行时 | .NET 10 + C# 14 | — | |
| UI 框架 | WPF + WPF-UI | `WPF-UI` 4.3 | |
| 数据库 | SQLite | — | 嵌入式, 无外部依赖 |
| SQLite 驱动 | Microsoft.Data.Sqlite | `Microsoft.Data.Sqlite` | 支持 LoadExtension 加载 sqlite-vec |
| 数据访问 | Dapper | `Dapper` | 轻量微 ORM, 手写 SQL + 自动映射 |
| Schema 版本化 | 嵌入式 SQL 脚本 + 版本表 | — | 轻量方案, 见下文 |
| 向量检索 | sqlite-vec | `sqlite-vec` (原生扩展) | SQLite 向量扩展, 通过 LoadExtension 加载 |
| AI 抽象 (Chat) | Microsoft.Extensions.AI | `Microsoft.Extensions.AI` | 统一 `IChatClient` 接口 |
| AI 抽象 (Embedding) | Microsoft.Extensions.AI | `Microsoft.Extensions.AI` | 统一 `IEmbeddingGenerator` 接口 |
| MVVM | CommunityToolkit.Mvvm | `CommunityToolkit.Mvvm` | Source Generator 驱动 |
| DI / 主机 | Microsoft.Extensions.Hosting | `Microsoft.Extensions.Hosting` | 通用主机管理生命周期 |
| 配置 | Microsoft.Extensions.Configuration | `Microsoft.Extensions.Configuration` + `.Json` + `.Options` | |
| 日志 | Serilog | `Serilog` + `Serilog.Extensions.Hosting` + `Serilog.Sinks.File` + `Serilog.Sinks.Async` | 结构化日志, 异步文件 sink |
| 序列化 | System.Text.Json | 内置 | |
| Markdown 解析 | Markdig | `Markdig` | 叙事内容 Markdown 解析 |
| Markdown 渲染 | Markdig → FlowDocument | — | 自定义 FlowDocument 渲染器, 原生 WPF, 无 WebView2 依赖 |
| MCP | ModelContextProtocol | `ModelContextProtocol` | 官方 .NET SDK, Server + Client |
| 弹性 | Polly | `Polly` + `Microsoft.Extensions.Http.Resilience` | AI 调用重试与熔断 |

## 数据访问层: Dapper

### 选型理由

本项目数据访问呈两极分化:

- **简单 CRUD**: 人物、状态、知识条目的增删改查 — Dapper 要手写 SQL, 比 EF Core 啰嗦
- **向量检索 + 复杂查询**: 知识检索、记忆召回、状态审计 — Dapper 原生 SQL 直接写, 是 EF Core 的不适区

EF Core 最核心的价值 (LINQ → SQL 翻译) 在向量查询中完全用不上。sqlite-vec 的 `vec0` 虚拟表、`MATCH` 操作符、blob 参数、动态 distance 列, EF Core 的 LINQ 翻译器均不认识, 每次都要降级到 `FromSqlRaw` + DTO + 绕限制。而本项目每轮导演指令保守估计触发 5-8 次向量查询, 集中在最关键的代码路径上。

Dapper 对向量查询和普通查询提供完全一致的 `QueryAsync<T>` 体验, 无降级, 无特殊处理。

### 痛点与缓解

- **手写 SQL**: 所有查询需手写 SQL, 表结构变更需手动检查相关 SQL
- **无迁移系统**: 通过轻量 schema 版本化机制缓解 (见下文)

### Schema 版本化

本项目 schema 在设计阶段基本定死 (人物、状态、知识、记忆、向量、配置、日志表), 上线后很少变动。采用轻量方案:

```
启动时检查 schema_version 表
    │
    ├─ 数据库不存在 → 执行 0001_init.sql 建表 → 记录版本 1
    ├─ 版本 1 → 执行 0002_add_audit_log.sql → 记录版本 2
    └─ 版本最新 → 跳过
```

SQL 脚本作为嵌入资源打包进程序。

## AI 模型配置: 双模型架构

Chat 模型和 Embedding 模型是两套独立的配置, 使用 Microsoft.Extensions.AI 的两个平行接口:

| 配置项 | 接口 | 用途 | 粒度 | 状态性 |
|--------|------|------|------|--------|
| Chat 模型 | `IChatClient` | 生成文本 (叙事、状态提取、审计判断等) | 每个 Agent 独立配置 | 无状态, 随时换模型不影响已存数据 |
| Embedding 模型 | `IEmbeddingGenerator<string, Embedding<float>>` | 文本 → 向量 (知识入库、记忆入库、查询检索) | 项目级唯一 | 有状态, 向量持久化, 换模型导致旧向量失效 |

### Chat 模型配置

每个 Agent 可以独立配置使用的 Chat 模型。例如:

- Narrator Agent → Claude Sonnet 4.5 (写得好, 贵)
- State Agent → GPT-4o-mini (提取结构化数据, 便宜)
- Audit Agent → Claude Haiku (判断力够就行)

Chat 模型无状态, 每次调用是独立的文本生成, 随时切换不影响任何已存储数据。

### Embedding 模型配置

#### 项目级绑定

每个剧本项目绑定自己的 Embedding 模型配置, 创建项目时选定。不同项目可以使用不同的 Embedding 模型, 互不影响。

#### 维度一致性约束

向量检索能工作的前提是所有向量在同一维度空间里。不同 Embedding 模型的维度不同 (OpenAI text-embedding-3-small 为 1536 维, Ollama nomic-embed-text 为 768 维), 即使维度碰巧相同, 不同模型的语义空间也不同。因此一个项目一旦选定 Embedding 模型, 不能随意更换。

#### 配置数据结构

```
Embedding 配置 {
    provider:    "openai" | "ollama" | "custom"
    endpoint:    "https://api.openai.com/v1" (或 http://localhost:11434)
    apiKey:      "***" (本地模型为空)
    modelName:   "text-embedding-3-small"
    dimension:   1536  ← 决定 sqlite-vec 虚拟表的列定义
}
```

#### 维度探测

创建项目时, 用户填写 Embedding 配置后点击"测试连接", 程序用一段示例文本调用一次 Embedding API, 自动探测返回维度, 填入 `dimension` 字段, 避免手填出错。

#### 更换 Embedding 模型 (重新嵌入)

如确需更换 Embedding 模型, 需执行"重新嵌入"操作:

```
检测到 Embedding 模型变更
    │
    ▼
提示用户: 所有已有向量将失效, 需要重新生成
    │
    ▼
遍历所有 knowledge_entries → 用新模型重新生成 embedding → 覆盖
遍历所有 memory_entries     → 用新模型重新生成 embedding → 覆盖
重建 sqlite-vec 虚拟表 (维度可能不同)
    │
    ▼
更新项目的 Embedding 模型配置记录
```

此为批量操作, UI 显示进度条, 期间锁定项目的检索功能。

## AI Provider 支持

### Chat 模型 Provider

统一通过 Microsoft.Extensions.AI 的 `IChatClient` 接口, 各 Provider 通过扩展包接入:

| Provider | 接入方式 | 说明 |
|----------|----------|------|
| OpenAI 兼容 | `OpenAI` 包 → `AsChatClient()` | 覆盖 OpenAI 官方及大量第三方兼容服务 |
| Ollama (本地) | `OllamaSharp` 或社区包 | 本地模型, 离线可用 |

### Embedding 模型 Provider

统一通过 Microsoft.Extensions.AI 的 `IEmbeddingGenerator` 接口:

| Provider | 接入方式 | 说明 |
|----------|----------|------|
| OpenAI 兼容 | `OpenAI` 包 → `AsEmbeddingGenerator()` | text-embedding-3-small / 3-large 等 |
| Ollama (本地) | `OllamaSharp` 或社区包 | nomic-embed-text / bge-m3 等, 离线免费 |

Chat 和 Embedding 可以指向同一个 Provider (比如都用 OpenAI), 也可以分开 (比如 Chat 用 Claude, Embedding 用 OpenAI 或本地 Ollama)。

## 设计原则

- 核心逻辑 (领域模型 + Agent 编排) 不依赖 UI 和具体存储实现, 可独立测试
- 所有 SQLite 操作通过 Dapper, 扩展加载 (sqlite-vec) 封装在基础设施层
- AI 调用全部经过 `IChatClient` / `IEmbeddingGenerator` 抽象, 不直接依赖任何 Provider SDK
- 配置驱动: Agent 定义、模型选择、压缩策略等均为可配置项, 不硬编码
- Chat 模型配置与 Embedding 模型配置完全独立, 互不耦合
