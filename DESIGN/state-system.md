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

system 驱动的值, 系统在特定时机按配置的规则变换, 变换后可以触发效果 (注入知识、改变基调等), 这些效果反过来影响叙事。

## valueType × driver 的常见组合

绝大多数场景落在三种组合里:

| 组合 | 示例 | 变化机制 |
|------|------|---------|
| numeric + narrative | 金钱 | AI 根据叙事加减 |
| enum + system | 天气 | 系统按概率/条件变换, 变换后注入知识/改变基调 |
| composite + system | 今日任务 | 系统触发生成条目, AI 在叙事中推进进度 |

少数场景也可能出现 composite + narrative (比如完全由 AI 管理的库存), 但 v1 重点是上面三种。

## config 结构

### numeric + narrative

```
{
    min: float?,
    max: float?,
    unit: string?,
    changeRules: string       // 自然语言, 指导 State Agent 如何提取变更
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
    effects: {                 // 变换到某值时的效果
        "暴风雨": {
            injectKnowledge: ["weather_storm"],
            directive: "叙事基调变为压抑"
        }
    }
}
```

`conditions` 是可选的 — 不配就是纯随机 (`transitionRules` 直接生效), 配了就是条件优先 (先匹配 conditions, 没匹配再走 transitionRules)。

### composite + system

```
{
    generationGuide: string,          // 自然语言, 指导 AI 生成条目
    regenerateTrigger: enum,          // scene_change / round_end / custom
    regenerateCondition: string?,     // 可选, 满足条件才重新生成
    itemCompleteEffect: Effect?,      // 条目完成时的效果
    itemFailEffect: Effect?           // 条目失败时的效果
}
```

composite 条目的生成是 system 触发的 (时机由系统控制), 但内容是 AI 生成的 (遵循生成指引)。条目的进度推进是 narrative 驱动的 (AI 在叙事中推进)。

## Effect: 状态变更的连锁反应

enum 变换和 composite 条目完成/失败时, 可以触发效果:

```
Effect {
    type: enum     // inject_knowledge / change_directive / update_state
    target: string
}
```

| 效果类型 | 作用 | 示例 |
|---------|------|------|
| inject_knowledge | 注入知识条目到叙事上下文 | 天气变暴风雨 → 注入暴风雨相关知识 |
| change_directive | 改变叙事基调 | 理智崩溃 → 基调变为混乱 |
| update_state | 联动更新其他状态属性 | 任务完成 → 评价 +5 |
Effect 是系统行为, 在状态变换时自动触发, 不经过 AI。
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

## 状态快照

每个场景结束时记录快照, 供回滚使用:

```
StateSnapshot {
    sceneId: long
    attributes: json      // { "money": 500, "weather": "雨", ... }
}
```

回滚到某场景时恢复该场景起点的快照。

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
| 时间线 | 状态快照按 sceneId 存储, 回滚时恢复对应场景的快照 |
| 知识系统 | Effect 的 inject_knowledge 向叙事注入知识 |
| 记忆系统 | 状态变更原因记录在 changeLog, Memory Sub-Agent 压缩时可参考 |
| 人物系统 | 复用 StateAttribute 模型, scope=category; 人物状态跟随分类继承解析 |
| 审计系统 | Audit Agent 用当前状态值 + changeLog 校验叙事一致性 |
| Agent 编排 | Memory Sub-Agent 负责提取 narrative 驱动的变更 (全局 + 人物); system 驱动的变换由系统自动执行 |
