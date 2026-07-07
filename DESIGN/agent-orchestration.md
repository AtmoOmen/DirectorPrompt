# 多 Agent 编排

## 架构基础: 依赖注入 + 事件序列

### 依赖注入

项目整体架构基于依赖注入。所有 Agent、服务、数据访问通过 DI 容器管理生命周期和依赖关系。

### 事件序列

一次游玩的过程本质上是一次可以重放的事件序列, 以确保后续的扩展性、可开发性。

每一轮导演交互产生一组事件, 按顺序追加到事件日志中。事件日志记录导演输入和叙事输出, 是对话历史的唯一事实来源。子系统的状态变更 (全局状态、人物状态、记忆、场景、生效指令等) 通过变更日志 (`round_changes`) 按轮次追踪, 回滚时逆序反转。

## Orchestrator: 确定性编排器

Orchestrator 是本地代码逻辑, 不是 AI。它负责决定每一轮导演指令进来后, 调哪些 Agent、以什么顺序、传什么上下文。它不做任何语义判断 — 语义判断全部交给各 Agent。

Orchestrator 的职责:
- 接收用户组合的指令批次
- 组装 Narrator 上下文
- 调度各 Agent 执行
- 记录事件
- 处理输出操作 (删除/编辑/修正/重写)

## 指令组合: 用户自选分类

用户不需要让 AI 或编排器去分类指令, 而是自己选择分类并组合。

### 输入区设计

输入区是一个指令组合器:

```
┌─────────────────────────────────────────────────────┐
│ 指令列表:                                             │
│ 1. [时间/场景变更] 三天后                    [↑] [↓] [✕] │
│ 2. [基调] 悬疑紧张                          [↑] [↓] [✕] │
│ 3. [剧情] 老王发现楼道渗水越来越严重了        [↑] [↓] [✕] │
│                                                      │
│ [+ 添加指令]  [分类选择 ▼] [输入内容] [添加]          │
│                                                      │
│ [发送]                                                │
└─────────────────────────────────────────────────────┘
```

用户可以:
- 添加多条不同分类的指令
- 调整顺序
- 删除单条指令
- 整批发送

### 指令分类

| 分类 | 说明 | 系统处理 |
|------|------|---------|
| 剧情 (默认) | 直接注入叙事事件 | 作为 Director Input 的核心 |
| 基调 | 约束叙事风格 | 加入 Active Directives, 带 TTL |
| 临时约束 | 短期硬性规则 | 加入 Active Directives, 带 TTL 或手动撤销 |
| 时间/场景变更 | 时间轴/场景切换 | 触发场景创建流程 |

### 顺序携带语义

指令的排列顺序携带语义, Narrator 按顺序理解:

**示例 1:**
```
1. [时间/场景变更] 三天后
2. [基调] 悬疑紧张
3. [剧情] 老王发现楼道渗水越来越严重了
```
语义: 三天后, 在紧张悬疑的氛围下, 描写老王发现楼道渗水越来越严重

**示例 2:**
```
1. [基调] 悬疑紧张
2. [剧情] 老王发现楼道渗水越来越严重了
3. [时间/场景变更] 三天后
```
语义: 在紧张悬疑的氛围下, 描写老王发现楼道渗水越来越严重这件事, 然后才是三天后

### 系统处理

指令批次发送后, Orchestrator 按顺序处理:
1. 时间/场景变更指令触发场景创建 (AI 通过 query_scene + create_scene 填写位置和 timeLabel)
2. 基调/临时约束指令加入 Active Directives
3. 全部指令按顺序组装为 Director Input, Narrator 看到完整的有序批次并理解序列语义

## 事件模型

### 事件基类

```
PlaythroughEvent {
    id: long               // 自增, 全局唯一
    projectId: long
    roundId: long          // 每轮导演交互有一个 roundId
    type: enum             // 事件类型, 见下文
    data: json             // 事件数据
    createdAt: datetime
}
```

同一轮的所有事件共享 `roundId`。

### 事件类型

| 事件类型 | 说明 | 数据 |
|---------|------|------|
| director_input | 用户指令批次 | 指令列表 (含分类、内容、顺序、是否系统指令) |
| narrative_output | AI 叙事输出 | 叙事文本 |
| state_change | 状态变更 | 属性名、旧值、新值、来源、原因、人物ID? |
| memory_update | 记忆更新 | 操作类型 (create/update/merge)、记忆ID、内容、tags、关联人物 |
| character_update | 人物更新 | 操作类型 (add/remove/update/enter_scene/leave_scene/relation_change/state_change)、人物数据 |
| scene_change | 场景切换 | 旧场景ID、新场景ID |
| directive_change | 指令变更 | 操作类型 (add/remove/expire)、指令内容、TTL |
| phase_transition | Phase 转换 | 当前激活的 Phase 集合 (用于下一轮进入/退出指令计算) |

### 变更追踪与回滚

系统采用变更日志模式, 而非纯事件溯源:

- **全局状态**: 通过 `state_change_logs` 表记录每次变更的 `old_value` / `new_value` / `round_id`, 回滚时取每属性在目标轮次最早的 `old_value` 恢复
- **其他表** (记忆、人物、关系、在场、生效指令、场景): 通过 `round_changes` 表记录每轮中所有 INSERT / UPDATE / DELETE 操作及其旧数据快照 (JSON), 回滚时按记录 ID 逆序反转

`RoundContext` (AsyncLocal) 在 `Orchestrator.ProcessBatchAsync` 中设置当前轮次 ID, 所有仓储在执行变更时自动检测上下文并记录到 `round_changes`。回滚操作在事务中执行, 确保原子性。

## 完整流水线

```
用户发送指令批次
    │
    ▼
Orchestrator 接收并处理批次 (本地代码)
    │
    ├─ Phase 评估: 与上一轮对比, 计算进入/退出转换
    ├─ 注入 Phase 系统指令 (进入指令 + 退出指令, 追加到用户指令开头)
    ├─ 时间/场景变更指令 → 触发场景创建 (AI 填位置)
    ├─ 基调/临时约束指令 → 加入 Active Directives
    └─ 全部指令按序组装为 Director Input
    │
    ▼
┌───── 并行检索阶段 ──────────────────────────────┐
│                                                 │
│  ┌─ Knowledge Agent ──┐                        │
│  │  三路混合检索        │                        │
│  │  (语义+实体+状态注入) │                        │
│  └────────┬───────────┘                        │
│           │                                     │
│  ┌─ Memory Sub-Agent ─┐                        │
│  │  召回相关记忆        │                        │
│  │  (语义+人物过滤)     │                        │
│  └────────┬───────────┘                        │
│           │                                     │
│  ┌─ 系统确定性注入 ────┐                        │
│  │  当前全局状态         │                        │
│  │  在场人物+关系网络    │                        │
│  │  场景时间信息         │                        │
│  │  Active Directives   │                        │
│  └────────┬───────────┘                        │
│           │                                     │
└───────────┼─────────────────────────────────────┘
            │
            ▼
    构造 Narrator 上下文
    (System + History + 上述注入 + Director Input)
            │
            ▼
    Narrator Agent 生成叙事
    (可调用 query_knowledge 主动查询)
            │
            ▼
    记录 director_input + narrative_output 事件
            │
            ▼
    Audit Agent 审计
    (分维度并行 → 去重合并 → 代码过滤 general)
            │
       ┌────┴────┐
       │         │
   通过/放行   阻断不通过
       │         │
       │         └→ 反馈 Narrator 重生成 ↑ (maxRetries)
       │
       ▼
┌───── 并行后处理阶段 ────────────────────────────┐
│                                                 │
│  ┌─ Memory Sub-Agent ──────────┐               │
│  │  从叙事提取并更新:            │               │
│  │  · 全局状态变更               │               │
│  │  · 人物状态变更               │               │
│  │  · 记忆更新                   │               │
│  │  · 人物维护 (增删改/关系)     │               │
│  │  · 在场状态维护               │               │
│  └────────┬────────────────────┘               │
│           │                                     │
└───────────┼─────────────────────────────────────┘
            │
            ▼
    系统执行状态变换
    (system 驱动的变换 + Phase 求值)
            │
            ▼
    记录所有衍生事件
    (state_change / memory_update / character_update / scene_change / directive_change)
            │
            ▼
    写入数据库 + 更新 UI
```

## 输出操作

对最新一次叙事输出, 用户可选择以下操作:

### 删除

回滚当前轮次的全部变更:
1. 逆序反转 `round_changes` 中该轮次的所有记录 (删除新建的行、恢复更新的行、重新插入删除的行)
2. 通过 `state_change_logs` 恢复全局状态到该轮次之前的值
3. 清理 `round_changes` 和 `state_change_logs` 中该轮次的记录
4. 删除 `playthrough_events` 中该轮次的事件

### 手动编辑

用户直接编辑叙事文本, 衍生事件 (状态/记忆/人物) 保持不变。

编辑完成后, 用户可选:
- **手动触发 Audit Agent**: 审计编辑后的文本, 显示问题但不自动修改
- **自行修改**: 根据审计结果手动调整
- **AI 辅助修改**: 让 AI 在当前文本基础上修改 (不是完整流水线重跑, 而是 AI 在现有文本上做局部修正)

手动编辑是"快速修正" — 改文本但不动系统状态。如果需要状态一致性, 应使用修正。

### 修正

用 (原文 + 用户上一次输入 + 用户的修正指引内容) 重新走一遍完整流水线。

```
修正流程:
    1. 用户输入修正指引内容
    2. 系统构造 Narrator 输入:
       - 原叙事输出 (作为参考)
       - 用户原始指令批次 (作为上下文)
       - 用户的修正指引内容 (作为实际指令)
    3. 重新走完整流水线 (检索 → 生成 → 审计 → 后处理)
    4. 展示修正后的叙事
    5. 用户选择:
       - 接受 → 替换原输出及其衍生事件, 提交新事件
       - 重试 → 重新修正 (再次走流程)
       - 拒绝 → 保持原输出不变, 丢弃修正结果
```

修正使用暂存 (staging) 机制: 流水线结果先暂存, 用户接受后才提交。拒绝则丢弃, 原输出不受影响。

修正过程中状态/记忆/人物的更新在暂存区进行, 只有用户接受时才真正写入事件序列。

### 重写

用用户上一次的原始输入重新走一遍完整流水线。

```
重写流程:
    1. 立即回退: 执行完整的删除操作 (反转 round_changes + 恢复 state_change_logs + 删除事件)
    2. 用原始指令批次重新走完整流水线
    3. 生成新输出, 记录新事件
    4. 不可取消 (事件已经回退, 无法恢复)
```

重写与修正的区别:
- **修正**: 有安全网 (可拒绝), 输入包含原文+原输入+修正指引, 是"知情的重做"
- **重写**: 无安全网 (不可取消), 输入只有原始指令, 是"从头的重做"

重写的回退通过 `DeleteRoundAsync` 完成, 该方法依次: 反转变更日志 → 恢复全局状态 → 清理日志 → 删除事件。

## 元指令与修正指令的处理

### 指令分类由用户完成

用户在指令组合器中自选分类, Orchestrator 不需要识别指令类型。但有两类操作是针对输出的, 不在指令批次中:

- **修正**: 针对最新输出, 走修正流程
- **重写**: 针对最新输出, 走重写流程
- **删除**: 针对最新输出, 直接移除
- **手动编辑**: 针对最新输出, 直接编辑

这些是 UI 上的操作按钮, 不是指令批次中的分类。

### 时间/场景变更

时间/场景变更是指令批次中的一个分类, 不是元指令。用户选择"时间/场景变更"分类, 输入自然语言描述 (如"三天后"), 系统触发场景创建流程。这发生在正常流水线开始前, 之后正常走检索 → 生成 → 审计 → 后处理。

## Agent 定义

每个 Agent 是一个可配置的单元:

```
AgentDefinition {
    name: string              // "Narrator" / "KnowledgeAgent" / ...
    role: enum                // narrator / knowledge / memory / state / audit / scene
    modelConfig: ModelConfig  // 使用哪个 Chat 模型
    systemPrompt: string      // Agent 的系统提示词
    temperature: float
    tools: string[]           // 可用的工具列表
    enabled: boolean
    maxRetries: int?          // 最大重试次数 (仅 Audit)
}

ModelConfig {
    provider: string          // "openai" / "anthropic" / "ollama" / "custom"
    endpoint: string
    apiKey: string?
    modelName: string
}
```

### 典型配置

| Agent | 模型 | 温度 | 说明 |
|-------|------|------|------|
| Narrator | 大模型 (Claude Sonnet / GPT-4o) | 0.7-0.9 | 生成叙事, 需要创造性 |
| Knowledge Agent | 中小模型 | 0.3 | 检索综合, 不需要创造性 |
| Memory Sub-Agent (召回) | Flash 模型 | 0.3 | 检索综合, 短上下文 |
| Memory Sub-Agent (更新+人物+状态) | Flash 模型 | 0.3 | 提取更新, 短上下文 |
| Audit Agent | 中小模型 | 0.1 | 判别式判断, 需要确定性 |
| Scene Agent | Flash 模型 | 0.3 | 场景创建, 生成 timeLabel |

### Memory Sub-Agent 的统一职责

Memory Sub-Agent 在后处理阶段统一负责:
- 全局状态变更提取
- 人物状态变更提取
- 记忆更新 (新建/改写/合并)
- 人物维护 (增删改/关系/在场)

这些操作都是"从叙事文本中提取信息并更新系统", 用一个 Agent 一次调用完成, 避免多次读取同一叙事文本。Narrator 不参与任何后处理。

## 并行度与依赖关系

```
                    ┌── Knowledge Agent ──┐
                    ├── Memory Sub-Agent ─┤
指令批次 ──────────┤  (召回)              ├──→ Narrator ──→ Audit ──→ Memory Sub-Agent ──→ 系统状态变换
                    ├── 系统确定性注入 ───┘    (可主动查询)     (分维度并行)    (更新+人物+状态)
                    │   (状态/人物/时间)
                    │
                    └── [并行, 无互相依赖]
```

### 可并行的

- Knowledge Agent 检索 + Memory Sub-Agent 召回 + 系统确定性注入 — 三者无依赖
- Audit 的各维度审计 — 互相独立

### 有依赖的

- Narrator 依赖 Knowledge + Memory + 系统注入 (需要它们的输出构造上下文)
- Audit 依赖 Narrator (审计的是 Narrator 的输出)
- Memory Sub-Agent (后处理) 依赖 Audit 通过 (审计不通过不执行后处理)

## 错误处理

### AI 调用失败

```
Agent 调用失败 (网络错误 / API 限流 / 超时)
    │
    ▼
Polly 重试 (指数退避, 默认 3 次)
    │
    ├─ 重试成功 → 继续
    └─ 重试耗尽 → 标记错误, 暂停流水线, UI 提示用户
```

### Tool 调用校验失败

Tool 参数校验失败 → 返回错误信息 → AI 重新调用。不终止流水线, 但设最大重试次数避免死循环。不存在幻觉处理, 就是参数校验拒绝重填。

### 审计死循环

Audit 阻断模式下, Narrator 和 Audit 之间可能死循环。通过 `maxRetries` 兜底 — 超过后标记警告放行。

## 配置

```
OrchestratorConfig {
    agents: AgentDefinition[]      // 各 Agent 的配置
    auditConfig: AuditConfig       // 审计配置 (见审计系统文档)
    memoryConfig: MemoryConfig     // 记忆配置 (见记忆系统文档)
    knowledgeConfig: KnowledgeRetrievalConfig  // 知识检索配置 (见知识系统文档)
}
```
