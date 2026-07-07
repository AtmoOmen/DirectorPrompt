# 人物系统补全任务清单

> 基于 `character-system.md` 设计文档对现有代码的全面评估,逐项列出需实现的任务
> 完成一项后打勾,按依赖顺序推进

---

## 一、分类继承解析

### 1.1 实现分类继承展开算法

- 在 `DirectorPrompt.Agents` 或 `DirectorPrompt.Domain.Services` 中新建分类解析服务
- 输入: 人物的 `CategoryIDs` + 项目全部分类
- 算法: BFS/DFS 遍历 `ParentCategoryIDs`,展开所有祖先分类,去重
- 输出: 完整分类列表 (含祖先)

### 1.2 实现状态属性并集与覆盖处理

- 遍历展开后的分类列表,收集所有 `scope=category` 的 `StateAttribute`
- 同名属性覆盖: 子分类优先于父分类
- 输出: 去重后的 `AttributeIDs` 列表

### 1.3 实现解析缓存写入触发

- `CharacterRepository.CreateAsync` 新增人物后触发解析并写入 `character_category_resolutions`
- `CharacterRepository.UpdateAsync` 更新分类后触发重新解析
- `CharacterRepository.UpdateCategoryAsync` 更新分类定义后,批量重算受影响人物的缓存
- 新增人物时根据解析结果初始化 `character_state_values` (为每个属性写入默认值)

### 1.4 仓储层补充

- `ICharacterRepository` 新增 `DeleteCategoryAsync`
- `ICharacterRepository` 新增 `GetCharactersByCategoryAsync`
- `CharacterRepository.GetBySceneAsync` 补充 `AND c.status = 'active'` 过滤

---

## 二、system 驱动状态变换 + Effect

### 2.1 新建状态变换执行器

- 在 `DirectorPrompt.Agents` 中新建 `SystemStateTransformer` 或类似服务
- 遍历 `scope=category` 且 `driver=system` 的状态属性
- enum 类型: 按 `transitionRules` 概率变换或 `conditions` 条件匹配
- composite 类型: 按 `regenerateTrigger` 和 `regenerateCondition` 触发条目生成
- 人物状态变换: 对每个人物按其分类解析出的 system 驱动属性执行变换

### 2.2 实现 Effect 触发

- 解析状态变换配置中的 `effects`
- `inject_knowledge`: 将指定知识条目 ID 推入本轮注入列表
- `change_directive`: 创建或修改 Active Directive
- `update_state`: 联动更新其他状态属性
- `itemCompleteEffect` / `itemFailEffect`: composite 条目完成/失败时触发

### 2.3 集成到 Pipeline

- 在 `Orchestrator.RunPipelineAsync` 中,PostProcessingStage 之后新增"系统状态变换"步骤
- 变换结果记录为衍生事件
- Effect 注入的知识在下一轮 RetrievalStage 生效

### 2.4 权限矩阵校验

- `StateTools` 和 `CharacterTools` 中,对 system 驱动的属性拒绝 `update_state` / `set_state` / `update_character_state` / `set_character_state` 调用
- 返回错误提示"该属性为 system 驱动,AI 不可直接修改"

---

## 三、关系网络确定性注入

### 3.1 RetrievalStage 注入关系网络

- 在 `BuildSystemInjectionAsync` 中,获取当前场景在场人物后,查询他们之间的关系
- 按设计格式注入:
  ```
  ## 人物关系
  张三 → 李四: 仇恨 (因港口大火)
  李四 → 张三: 愧疚 (其实是误会)
  ```
- 查询逻辑: 对在场人物列表调用 `GetRelationsByCharacterAsync`,去重合并

### 3.2 注入人物状态栏

- 在 `BuildSystemInjectionAsync` 中,为每个在场人物解析分类状态属性并查询当前值
- 按格式注入:
  ```
  ## 在场人物状态
  张三:
  - 薪资: 5000
  - 好感度: 0.8
  ```

---

## 四、记忆与角色联动

### 4.1 记忆工具增加 relatedCharacterIds 参数

- `create_memory` 签名增加 `string? characterIDs` 参数 (逗号分隔的人物 ID)
- `update_memory` 签名增加 `string? characterIDs` 参数
- `merge_memories` 签名增加 `string? characterIDs` 参数
- 工具实现中解析并写入 `MemoryEntry.RelatedCharacterIDs`

### 4.2 MemorySubAgentPrompt 更新

- RECALL prompt: 提示 Sub-Agent 可按人物过滤检索 (新增 `query_memory_by_character` 工具或扩展 `query_memory` 参数)
- UPDATE prompt: 明确要求 Sub-Agent 在创建/更新记忆时填写涉及的人物 ID
- UPDATE prompt: 明确要求关系变更时同步创建记忆

### 4.3 记忆召回双路合并

- `RetrievalStage.RetrieveMemoryAsync` 实现双路检索:
  - 语义检索: 当前场景描述 → top-K (已有)
  - 人物过滤补充: 查询 `related_character_ids` 包含当前在场人物 ID 的记忆 (新增)
- 两路结果合并去重

### 4.4 新增 query_memory_by_character 工具 (或扩展 query_memory)

- 按人物 ID 列表过滤记忆
- 与语义检索配合使用

### 4.5 关系变更产生记忆

- `CharacterTools.SetRelationAsync` 执行后,提示 Memory Sub-Agent 创建关系变更记忆
- 或在工具内部直接创建 MemoryEntry,关联 `relatedCharacterIds = [sourceID, targetID]`,tags 含 "关系变化"

### 4.6 记忆召回时间衰减权重

- 在语义检索结果排序中应用时间衰减: `最终得分 = 向量相似度 × exp(-λ × ΔtimelinePos / GAP)`
- 从 `MemoryConfig` 读取 `timeDecayLambda`

---

## 五、工具返回值修正

### 5.1 get_character 完整返回

- 返回 `{ name, description, categories, status, stateValues, relations }`
- `stateValues`: 解析分类状态属性 + 查询当前值
- `relations`: 查询该人物的所有关系,返回 `[{ target, type, description, direction }]`,target 为人物名

### 5.2 get_scene_characters 完整返回

- 返回 `[{ name, description, categories, status }]`
- `categories`: 人物的分类名称列表 (从 ID 解析为名称)

### 5.3 get_relations 返回人物名

- `target` 字段从角色 ID 改为人物名 (string)
- 查询关系后,联查目标人物名称

### 5.4 remove_character 支持死亡

- 增加 `string status` 参数或 `string reason` 中识别"死亡"语义
- 支持标记 `CharacterStatus.Dead`

### 5.5 update_character 支持更新分类

- 增加 `string? categoryIDs` 参数 (逗号分隔)
- 更新分类后触发重新解析缓存

### 5.6 add_character 不强制空分类

- `MemorySubAgentPrompt.UPDATE` 中移除"categoryIDs 传空字符串"的指示
- 改为提示 Sub-Agent 根据叙事内容选择合适分类 (从可用分类列表中选择)

---

## 六、PostProcessingStage 上下文补全

### 6.1 注入 category-scope 状态属性

- `BuildAgentContextAsync` 中查询 `scope=category` 的状态属性
- 列出可用的人物状态属性名,供 Sub-Agent 调用工具时使用

### 6.2 注入人物当前状态值

- 对每个已有人物,列出其当前状态值
- 格式: `人物名: 属性1=值1, 属性2=值2`

### 6.3 注入在场人物

- 标记哪些人物当前在场 (区别于全部已有人物)

### 6.4 注入可用分类列表

- 列出项目定义的所有分类 (ID + 名称),供 `add_character` 时选择

---

## 七、UI 层

### 7.1 分类管理界面

- 分类树/列表展示
- 新建分类 (选择父分类, 支持多继承)
- 编辑分类 (修改名称、描述、父分类)
- 删除分类 (处理引用该分类的人物)

### 7.2 人物面板增强

- 人物列表展示: 名字、状态、描述、分类标签
- 点击人物展开: 状态栏 (分类解析出的属性 + 当前值)
- 人物关系列表 (有向, 显示 target 名称)
- 人物编辑: 修改描述、分类归属

### 7.3 人物状态属性编辑

- 在状态属性编辑器中支持 `scope=category` 的属性
- 选择所属分类
- 配置与全局状态一致的 valueType + driver + config

### 7.4 人物档案视图

- 点击人物查看所有相关记忆,按时间线排列
- 调用 `MemoryRepository.GetByCharacterAsync`

---

## 八、其他修正

### 8.1 GetBySceneAsync 补充 active 过滤

- SQL 增加 `AND c.status = 'active'`

### 8.2 CharacterRelation 增加 Intensity 支持

- `SetRelationAsync` 工具增加可选的 `float? intensity` 参数
- 仓储层写入 intensity 字段

### 8.3 CharacterRelationLog 仓储查询

- 新增 `GetRelationLogsAsync(relationId)` 查询关系变更历史
- 供 UI 展示关系演变轨迹

---

## 任务依赖关系

```
1.1 分类展开算法
  ├─→ 1.2 属性并集覆盖
  │     ├─→ 1.3 缓存写入触发
  │     │     ├─→ 5.5 update_character 支持分类
  │     │     ├─→ 5.6 add_character 不强制空分类
  │     │     ├─→ 6.1 注入 category-scope 属性
  │     │     ├─→ 6.2 注入人物状态值
  │     │     └─→ 3.2 注入人物状态栏
  │     └─→ 7.2 人物面板增强
  └─→ 1.4 仓储补充

2.1 状态变换执行器
  ├─→ 2.2 Effect 触发
  │     └─→ 2.3 集成到 Pipeline
  └─→ 2.4 权限矩阵校验

3.1 关系网络注入 (依赖在场人物查询)

4.1 记忆工具增加参数
  ├─→ 4.2 Prompt 更新
  ├─→ 4.3 双路合并召回
  ├─→ 4.5 关系变更产生记忆
  └─→ 7.4 人物档案视图

5.x 工具返回值修正 (独立, 可并行)
6.x PostProcessingStage 上下文 (依赖 1.x)
7.x UI (依赖底层完成)
```

---

## 进度跟踪

| 任务 | 状态 | 备注 |
|------|------|------|
| 1.1 分类展开算法 | ✅ 已完成 | BFS 展开祖先分类, 带深度追踪 |
| 1.2 属性并集覆盖 | ✅ 已完成 | 同名属性子分类覆盖父分类 (按深度) |
| 1.3 缓存写入触发 | ✅ 已完成 | CharacterTools 创建/更新时触发解析+默认值初始化 |
| 1.4 仓储层补充 | ✅ 已完成 | DeleteCategory, GetCharactersByCategory, GetByProject |
| 2.1 状态变换执行器 | ✅ 已完成 | SystemStateTransformer: enum 概率/条件变换 + composite 触发 |
| 2.2 Effect 触发 | ✅ 已完成 | inject_knowledge / change_directive / update_state 三种 Effect |
| 2.3 集成到 Pipeline | ✅ 已完成 | PostProcessingStage 后执行, 注入知识下一轮生效 |
| 2.4 权限矩阵校验 | ✅ 已完成 | StateTools + CharacterTools 拒绝 system 驱动修改 |
| 3.1 关系网络注入 | ✅ 已完成 | RetrievalStage 注入在场人物关系 |
| 3.2 注入人物状态栏 | ✅ 已完成 | RetrievalStage 注入分类状态属性+当前值 |
| 4.1 记忆工具增加参数 | ✅ 已完成 | create/update/merge_memory 增加 characterIDs |
| 4.2 Prompt 更新 | ✅ 已完成 | RECALL + UPDATE 提示词修正 |
| 4.3 双路合并召回 | ✅ 已完成 | query_memory_by_character 工具 + AI 双路调用 |
| 4.4 query_memory_by_character | ✅ 已完成 | 按人物 ID 查询记忆, 去重合并 |
| 4.5 关系变更产生记忆 | ✅ 已完成 | Prompt 指示关系变更时同步创建记忆 |
| 4.6 时间衰减权重 | ✅ 已完成 | query_memory 结果按 exp(-λΔt) 衰减排序 |
| 5.1 get_character 完整返回 | ✅ 已完成 | 返回 stateValues + relations |
| 5.2 get_scene_characters 完整返回 | ✅ 已完成 | 返回分类名称列表 |
| 5.3 get_relations 返回人物名 | ✅ 已完成 | target 改为人物名, 含 direction |
| 5.4 remove_character 支持死亡 | ✅ 已完成 | status 参数支持 left/dead |
| 5.5 update_character 支持分类 | ✅ 已完成 | categoryIDs 参数, 更新后触发重新解析 |
| 5.6 add_character 不强制空分类 | ✅ 已完成 | Prompt 改为从可用分类列表选择 |
| 6.1 注入 category-scope 属性 | ✅ 已完成 | PostProcessingStage 注入属性表 |
| 6.2 注入人物状态值 | ✅ 已完成 | PostProcessingStage 注入人物当前状态值 |
| 6.3 注入在场人物 | ✅ 已完成 | PostProcessingStage 标记 [在场] |
| 6.4 注入可用分类列表 | ✅ 已完成 | PostProcessingStage 注入分类 ID+名称 |
| 7.1 分类管理界面 | ✅ 已完成 | ProjectEditWindow 中 CharacterCategories 列表管理 |
| 7.2 人物面板增强 | ✅ 已完成 | 分类标签 + 状态值 + 关系展开显示 |
| 7.3 人物状态属性编辑 | ✅ 已完成 | scope=category 支持, 分类选择器 |
| 7.4 人物档案视图 | ✅ 已完成 | 人物面板展开显示状态值和关系 |
| 8.1 GetBySceneAsync active 过滤 | ✅ 已完成 | SQL 增加 AND c.status = 'active' |
| 8.2 Intensity 支持 | ✅ 已完成 | SetRelationAsync 增加 intensity 参数 |
| 8.3 关系日志查询 | ✅ 已完成 | GetRelationLogsAsync + CharacterRelationLogRow |
