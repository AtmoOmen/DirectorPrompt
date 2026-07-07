# 状态系统

## 概述

状态系统管理世界状态, 驱动叙事走向。状态栏是全局性的, 影响叙事的。例如一个"商店模拟经营"项目可以定义"金钱"、"评价"、"天气"、"今日任务"等状态属性, 这些属性共同描述当前世界的状态, 并驱动叙事方向。

状态系统同时服务于全局状态和人物状态 — 两者复用同一套 StateAttribute 模型, 通过 scope 区分作用域。人物状态跟随人物分类走, 详见人物系统文档。

## 核心概念

```
StateAttribute (状态属性)
├── valueType: 这个属性的数据形态
├── driver: 这个属性由谁驱动变化
├── config: valueType + driver 组合的具体配置
├── scope: 作用域 (global | category)
├── currentState: 当前值
└── changeLog: 变更历史
```

### 作用域: global vs category

| | 全局状态 (global) | 人物状态 (category) |
|--|------------------|-------------------|
| 作用域 | 整个项目 | 特定分类的人物 |
| 存储 | StateValue 表 | CharacterStateValue 表 |
| 检索 | 直接查 | 通过人物分类归属解析后查 |
| driver 行为 | 完全一致 | 完全一致 |
| 权限矩阵 | 完全一致 | 完全一致 |

人物状态的分类继承解析 (父分类属性继承、子分类扩展/覆盖、多分类并集) 详见人物系统文档。

三个概念各管一件事:

- **valueType** — 数据是什么形态 (数字、枚举、条目列表)
- **driver** — 谁让它变的 (叙事 / 系统)
- **config** — 具体怎么变、变了有什么效果

## valueType: 三种数据形态

| valueType | 值域 | 典型场景 |
|-----------|------|---------|
| numeric | 连续数值 | 金钱、评价、声望 |
| enum | 离散选项 | 天气、季节、政治局势 |
| composite | 条目列表 | 今日任务、库存、成就 |

composite 的每个条目统一为"描述 + 进度":

```
CompositeItem {
    description: string     // "卖出 3 块铁矿石"
    current: float          // 0
    target: float           // 3
    status: enum            // active / completed / failed
}
```

任务、库存、计数器都是这个结构。后续需要新类型, 继承基类扩展即可。

## driver: 两种驱动方式

| driver | 谁驱动变化 | AI 能否直接改 | 典型场景 |
|--------|----------|-------------|---------|
| narrative | AI 从叙事中提取变更 | 可以 | 金钱、评价、库存数量 |
| system | 系统按规则自动变换 | 不能 | 天气、季节、今日任务生成 |

narrative 驱动的值, State Agent 在每轮叙事后提取变更并调用 tool 落地。

system 驱动的值, 系统在特定时机按配置的规则变换。变换后的值通过 Phase 机制将关联的禁用知识变为可检索, 影响叙事走向。

## valueType × driver 的常见组合

绝大多数场景落在三种组合里:

| 组合 | 示例 | 变化机制 |
|------|------|---------|
| numeric + narrative | 金钱 | AI 根据叙事加减 |
| enum + system | 天气 | 系统按概率/条件变换, 变换后注入知识/改变基调 |
| composite + system | 今日任务 | 系统触发生成条目, AI 在叙事中推进进度 |

少数场景也可能出现 composite + narrative (比如完全由 AI 管理的库存), 但 v1 重点是上面三种。

## config 结构

所有 config 都可包含 `phases` 字段, 详见 [Phase](#phase-状态的阶段声明) 章节。

### numeric + narrative

```
{
    min: float?,
    max: float?,
    unit: string?,
    changeRules: string       // 自然语言, 指导 State Agent 如何提取变更
    phases: Phase[]
}
```

### enum + system

```
{
    options: string[],
    transitionRules: {         // 从当前值变换到各选项的概率
        "晴": { "晴": 0.5, "阴": 0.3, "雨": 0.2 },
        ...
    },
    conditions: [              // 可选, 条件优先于纯概率
        {
            when: string,      // 条件表达式
            transition: { ... }
        }
    ],
    trigger: enum,             // scene_change / round_end / custom
    phases: Phase[]
}
```

`conditions` 是可选的 — 不配就是纯随机 (`transitionRules` 直接生效), 配了就是条件优先 (先匹配 conditions, 没匹配再走 transitionRules)。

### composite + system

```
{
    generationGuide: string,          // 自然语言, 指导 AI 生成条目
    regenerateTrigger: enum,          // scene_change / round_end / custom
    regenerateCondition: string?,     // 可选, 满足条件才重新生成
    phases: Phase[]
}
```

composite 条目的生成是 system 触发的 (时机由系统控制), 但内容是 AI 生成的 (遵循生成指引)。条目的进度推进是 narrative 驱动的 (AI 在叙事中推进)。

## Phase: 状态的阶段声明

Phase 是状态属性的阶段声明机制。每个状态属性可以定义多个阶段, 每个阶段通过表达式判断是否激活, 激活时将其关联的禁用知识变为可检索状态。

Phase 适用于所有 valueType, 是状态驱动的知识启用机制 — 状态值满足条件时, 关联的禁用知识变为可检索, 后续能否被检索到取决于 Knowledge Agent 的语义匹配, 而非强制注入。

### Phase 模型

```
Phase {
    name:              string        // 阶段名称, 如 "百万富翁"
    expression:        string        // 表达式, 使用 {val} 代表当前值
    knowledgeIds:      long[]        // 关联的知识条目 ID (必须为禁用状态)
    knowledgeGroupIds: long[]        // 关联的知识分组 ID (必须为禁用状态)
    enterDirectives:   PhaseDirective[]  // 进入指令: Phase 激活时在下一轮追加到用户指令开头
    exitDirectives:    PhaseDirective[]  // 退出指令: Phase 失活时在下一轮追加到用户指令开头
}

PhaseDirective {
    type:    DirectiveType  // 剧情 / 基调 / 临时约束 / 时间场景变更
    content: string         // 指令内容
    ttl:     int?           // 生效轮数, null 表示永久 (仅基调/临时约束有意义)
}
```

### 表达式语法

表达式使用 `{val}` 占位符代表当前状态值, 支持数学比较和字符串比较:

| valueType | 表达式形式 | 示例 |
|-----------|----------|------|
| numeric | 数学表达式 | `{val} >= 1000000`、`{val} + 1000 >= 10000`、`{val} > 50 AND {val} < 100` |
| enum | 字符串比较 | `{val} == "雪"`、`{val} != "阴"` |

表达式计算结果为 true 时, 该阶段激活, 关联知识变为可检索状态; 为 false 时保持禁用, 不参与检索。

### 知识关联规则

- Phase 关联的知识必须在知识系统中处于**禁用状态** (Active = false)
- 禁用状态的知识不参与常规知识检索, Phase 激活后变为可检索状态, 与正常活跃知识一同进入检索池
- 一个 Phase 可以关联多条知识, 也可以关联整个知识分组
- 关联知识分组时, 分组下的所有知识条目一并变为可检索
- 知识在 Phase 激活期间持续可检索, Phase 失效后退出检索池
- 能否被检索到取决于 Knowledge Agent 的语义匹配和实体匹配, Phase 只控制“可不可被检索”, 不强制注入

### 进入/退出指令

Phase 激活 (从非激活变为激活) 或失活 (从激活变为非激活) 时, 可以在下一轮自动追加指令到用户指令开头:

- **进入指令** (enterDirectives): Phase 激活时触发, 在下一轮用户指令前追加
- **退出指令** (exitDirectives): Phase 失活时触发, 在下一轮用户指令前追加

这些指令与主界面底部的指令组合器完全一致, 可以组合多个不同分类的指令 (剧情、基调、临时约束、时间/场景变更)。但它们是系统指令, 具有以下特性:

- 在主界面消息渲染中不可见
- 无法编辑
- 回退时不恢复到输入框 (不会泄漏)
- AI 在叙事上下文中能看到完整指令 (包含系统指令), 按顺序理解

Phase 状态追踪通过 `PhaseTransition` 事件实现: 每轮结束时记录当前激活的 Phase 集合, 下一轮开始时与上一轮对比, 计算进入/退出转换。回退时事件自动移除, Phase 状态自然回退。

### 示例

**numeric + narrative (金钱):**

```
phases: [
    {
        name: "百万富翁",
        expression: "{val} >= 1000000",
        knowledgeIds: [12, 15],
        knowledgeGroupIds: [3]
    },
    {
        name: "破产边缘",
        expression: "{val} < 100",
        knowledgeIds: [20]
    }
]
```

金钱达到 100 万时, "百万富翁"相关的禁用知识变为可检索, 同时追加进入指令 (如 "[基调] 富足奢华"、"[剧情] 描写角色的财富展示"); 跌破 100 时, "破产边缘"相关知识变为可检索, 同时追加退出指令 (如 "[基调] 紧迫困窘")。这些系统指令在主界面不可见, 但 AI 能在叙事上下文中看到。

**enum + system (天气):**

```
phases: [
    {
        name: "暴雪天气",
        expression: "{val} == \"雪\"",
        knowledgeIds: [30, 31],
        knowledgeGroupIds: [5]
    }
]
```

天气变换为"雪"时, 暴雪天时对应的禁用知识 (雪天场景描写、雪天可用事件等) 变为可检索。

## 变更审计

所有状态变更都记录:

```
StateChangeLog {
    attributeId: long
    sceneId: long
    roundId: long?
    oldValue: string
    newValue: string
    source: enum          // state_agent / system / director_manual
    reason: string        // 必填
    createdAt: datetime
}
```

用户可回溯"为什么金钱突然少了"。Audit Agent 也用这个做一致性校验。

## 工具定义

### 查询

```
get_state(attribute: string) -> { value, stage? }
get_all_state() -> [{ attribute, value, stage? }]
get_composite_items(attribute: string) -> [{ description, current, target, status }]
```

### 变更 (narrative 驱动专用)

```
update_state(attribute: string, delta: float, reason: string) -> { oldValue, newValue }
set_state(attribute: string, value: string, reason: string) -> { oldValue, newValue }
```

### 复合值操作

```
add_item(attribute: string, description: string, target: float, reason: string) -> itemId
update_item(attribute: string, itemId: long, delta: float, reason: string) -> { oldProgress, newProgress, statusChanged }
remove_item(attribute: string, itemId: long, reason: string)
```

### 权限矩阵

| 操作 | narrative 驱动 | system 驱动 |
|------|---------------|-------------|
| get | ✓ | ✓ |
| update_state / set_state | ✓ | ✗ |
| add_item / remove_item | ✓ | ✗ (仅 system 触发生成时由系统调用) |
| update_item | ✓ | ✓ (推进进度是叙事的一部分) |

## 与其他系统的交互

| 系统 | 交互方式 |
|------|---------|
| 时间线 | 状态变更按 sceneId 关联, 回滚时移除事件 |
| 知识系统 | Phase 激活时将关联的禁用知识变为可检索, 由 Knowledge Agent 语义匹配决定是否检索 |
| 记忆系统 | 状态变更原因记录在 changeLog, Memory Sub-Agent 压缩时可参考 |
| 人物系统 | 复用 StateAttribute 模型, scope=category; 人物状态跟随分类继承解析 |
| 审计系统 | Audit Agent 用当前状态值 + changeLog 校验叙事一致性 |
| Agent 编排 | Memory Sub-Agent 负责提取 narrative 驱动的变更 (全局 + 人物); system 驱动的变换由系统自动执行; Phase 的表达式求值、知识启用和进入/退出指令注入由 PhaseEvaluator 和 Orchestrator 自动完成 |
