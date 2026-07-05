-- 项目
CREATE TABLE IF NOT EXISTS projects
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    name
    TEXT
    NOT
    NULL,
    world_overview
    TEXT
    NOT
    NULL
    DEFAULT
    '',
    narrative_style
    TEXT
    NOT
    NULL
    DEFAULT
    '',
    permanent_constraints
    TEXT
    NOT
    NULL
    DEFAULT
    '[]',
    created_at
    TEXT
    NOT
    NULL,
    updated_at
    TEXT
    NOT
    NULL
);

-- 场景
CREATE TABLE IF NOT EXISTS scenes
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    project_id
    INTEGER
    NOT
    NULL,
    timeline_position
    INTEGER
    NOT
    NULL,
    time_label
    TEXT
    NOT
    NULL,
    summary
    TEXT,
    status
    TEXT
    NOT
    NULL
    DEFAULT
    'active',
    FOREIGN
    KEY
(
    project_id
) REFERENCES projects
(
    id
)
    );

CREATE INDEX IF NOT EXISTS idx_scenes_project_timeline ON scenes(project_id, timeline_position);
CREATE INDEX IF NOT EXISTS idx_scenes_project_status ON scenes(project_id, status);

-- 轮次
CREATE TABLE IF NOT EXISTS rounds
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    project_id
    INTEGER
    NOT
    NULL,
    scene_id
    INTEGER
    NOT
    NULL,
    created_at
    TEXT
    NOT
    NULL,
    FOREIGN
    KEY
(
    project_id
) REFERENCES projects
(
    id
),
    FOREIGN KEY
(
    scene_id
) REFERENCES scenes
(
    id
)
    );

-- 事件
CREATE TABLE IF NOT EXISTS playthrough_events
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    project_id
    INTEGER
    NOT
    NULL,
    round_id
    INTEGER
    NOT
    NULL,
    type
    TEXT
    NOT
    NULL,
    data
    TEXT
    NOT
    NULL,
    created_at
    TEXT
    NOT
    NULL,
    FOREIGN
    KEY
(
    project_id
) REFERENCES projects
(
    id
)
    );

CREATE INDEX IF NOT EXISTS idx_events_project_round ON playthrough_events(project_id, round_id);
CREATE INDEX IF NOT EXISTS idx_events_project ON playthrough_events(project_id);

-- 状态属性
CREATE TABLE IF NOT EXISTS state_attributes
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    project_id
    INTEGER
    NOT
    NULL,
    name
    TEXT
    NOT
    NULL,
    display_name
    TEXT
    NOT
    NULL,
    scope
    TEXT
    NOT
    NULL
    DEFAULT
    'global',
    category_id
    INTEGER,
    value_type
    TEXT
    NOT
    NULL,
    driver
    TEXT
    NOT
    NULL,
    config
    TEXT
    NOT
    NULL
    DEFAULT
    '{}',
    FOREIGN
    KEY
(
    project_id
) REFERENCES projects
(
    id
)
    );

CREATE INDEX IF NOT EXISTS idx_state_attrs_project_scope ON state_attributes(project_id, scope);
CREATE INDEX IF NOT EXISTS idx_state_attrs_category ON state_attributes(category_id);

-- 状态值
CREATE TABLE IF NOT EXISTS state_values
(
    attribute_id
    INTEGER
    PRIMARY
    KEY,
    value
    TEXT
    NOT
    NULL
    DEFAULT
    '',
    updated_at
    TEXT
    NOT
    NULL,
    FOREIGN
    KEY
(
    attribute_id
) REFERENCES state_attributes
(
    id
)
    );

-- 复合值条目
CREATE TABLE IF NOT EXISTS composite_items
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    attribute_id
    INTEGER
    NOT
    NULL,
    description
    TEXT
    NOT
    NULL,
    current
    REAL
    NOT
    NULL
    DEFAULT
    0,
    target
    REAL
    NOT
    NULL
    DEFAULT
    0,
    status
    TEXT
    NOT
    NULL
    DEFAULT
    'active',
    FOREIGN
    KEY
(
    attribute_id
) REFERENCES state_attributes
(
    id
)
    );

CREATE INDEX IF NOT EXISTS idx_composite_items_attr ON composite_items(attribute_id);

-- 标记
CREATE TABLE IF NOT EXISTS flags
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    project_id
    INTEGER
    NOT
    NULL,
    name
    TEXT
    NOT
    NULL,
    display_name
    TEXT
    NOT
    NULL
    DEFAULT
    '',
    value
    INTEGER
    NOT
    NULL
    DEFAULT
    0,
    set_at_scene_id
    INTEGER,
    FOREIGN
    KEY
(
    project_id
) REFERENCES projects
(
    id
)
    );

CREATE UNIQUE INDEX IF NOT EXISTS idx_flags_project_name ON flags(project_id, name);

-- 状态变更日志
CREATE TABLE IF NOT EXISTS state_change_logs
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    attribute_id
    INTEGER
    NOT
    NULL,
    scene_id
    INTEGER
    NOT
    NULL,
    round_id
    INTEGER,
    old_value
    TEXT
    NOT
    NULL
    DEFAULT
    '',
    new_value
    TEXT
    NOT
    NULL
    DEFAULT
    '',
    source
    TEXT
    NOT
    NULL,
    reason
    TEXT
    NOT
    NULL
    DEFAULT
    '',
    created_at
    TEXT
    NOT
    NULL,
    FOREIGN
    KEY
(
    attribute_id
) REFERENCES state_attributes
(
    id
)
    );

CREATE INDEX IF NOT EXISTS idx_change_logs_attr ON state_change_logs(attribute_id);
CREATE INDEX IF NOT EXISTS idx_change_logs_scene ON state_change_logs(scene_id);

-- 状态快照
CREATE TABLE IF NOT EXISTS state_snapshots
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    project_id
    INTEGER
    NOT
    NULL,
    round_id
    INTEGER
    NOT
    NULL,
    global_state
    TEXT
    NOT
    NULL
    DEFAULT
    '{}',
    character_state
    TEXT
    NOT
    NULL
    DEFAULT
    '{}',
    flags
    TEXT
    NOT
    NULL
    DEFAULT
    '{}',
    active_directives
    TEXT
    NOT
    NULL
    DEFAULT
    '{}',
    current_scene_id
    INTEGER
    NOT
    NULL,
    scene_characters
    TEXT
    NOT
    NULL
    DEFAULT
    '[]',
    created_at
    TEXT
    NOT
    NULL,
    FOREIGN
    KEY
(
    project_id
) REFERENCES projects
(
    id
)
    );

CREATE INDEX IF NOT EXISTS idx_snapshots_project_round ON state_snapshots(project_id, round_id);
CREATE INDEX IF NOT EXISTS idx_snapshots_scene ON state_snapshots(current_scene_id);

-- 生效指令
CREATE TABLE IF NOT EXISTS active_directives
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    project_id
    INTEGER
    NOT
    NULL,
    type
    TEXT
    NOT
    NULL,
    content
    TEXT
    NOT
    NULL,
    ttl
    INTEGER,
    created_at
    TEXT
    NOT
    NULL,
    FOREIGN
    KEY
(
    project_id
) REFERENCES projects
(
    id
)
    );

CREATE INDEX IF NOT EXISTS idx_directives_project ON active_directives(project_id);

-- 知识条目
CREATE TABLE IF NOT EXISTS knowledge_entries
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    project_id
    INTEGER
    NOT
    NULL,
    title
    TEXT
    NOT
    NULL,
    content
    TEXT
    NOT
    NULL,
    tags
    TEXT
    NOT
    NULL
    DEFAULT
    '[]',
    group_id
    INTEGER,
    active
    INTEGER
    NOT
    NULL
    DEFAULT
    1,
    created_at
    TEXT
    NOT
    NULL,
    updated_at
    TEXT
    NOT
    NULL,
    FOREIGN
    KEY
(
    project_id
) REFERENCES projects
(
    id
)
    );

CREATE INDEX IF NOT EXISTS idx_knowledge_project_active ON knowledge_entries(project_id, active);

-- 知识分组
CREATE TABLE IF NOT EXISTS knowledge_groups
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    project_id
    INTEGER
    NOT
    NULL,
    name
    TEXT
    NOT
    NULL,
    description
    TEXT,
    active
    INTEGER
    NOT
    NULL
    DEFAULT
    1,
    FOREIGN
    KEY
(
    project_id
) REFERENCES projects
(
    id
)
    );

-- 知识实体索引
CREATE TABLE IF NOT EXISTS knowledge_entity_index
(
    entry_id
    INTEGER
    NOT
    NULL,
    entity_name
    TEXT
    NOT
    NULL,
    PRIMARY
    KEY
(
    entry_id,
    entity_name
),
    FOREIGN KEY
(
    entry_id
) REFERENCES knowledge_entries
(
    id
)
    );

CREATE INDEX IF NOT EXISTS idx_entity_index_name ON knowledge_entity_index(entity_name);

-- 记忆条目
CREATE TABLE IF NOT EXISTS memory_entries
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    project_id
    INTEGER
    NOT
    NULL,
    scene_id
    INTEGER
    NOT
    NULL,
    timeline_pos
    INTEGER
    NOT
    NULL,
    content
    TEXT
    NOT
    NULL,
    tags
    TEXT
    NOT
    NULL
    DEFAULT
    '[]',
    related_character_ids
    TEXT
    NOT
    NULL
    DEFAULT
    '[]',
    created_at
    TEXT
    NOT
    NULL,
    updated_at
    TEXT
    NOT
    NULL,
    FOREIGN
    KEY
(
    project_id
) REFERENCES projects
(
    id
),
    FOREIGN KEY
(
    scene_id
) REFERENCES scenes
(
    id
)
    );

CREATE INDEX IF NOT EXISTS idx_memory_project_timeline ON memory_entries(project_id, timeline_pos);
CREATE INDEX IF NOT EXISTS idx_memory_scene ON memory_entries(scene_id);

-- 人物
CREATE TABLE IF NOT EXISTS characters
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    project_id
    INTEGER
    NOT
    NULL,
    name
    TEXT
    NOT
    NULL,
    description
    TEXT
    NOT
    NULL
    DEFAULT
    '',
    category_ids
    TEXT
    NOT
    NULL
    DEFAULT
    '[]',
    status
    TEXT
    NOT
    NULL
    DEFAULT
    'active',
    created_at
    TEXT
    NOT
    NULL,
    updated_at
    TEXT
    NOT
    NULL,
    FOREIGN
    KEY
(
    project_id
) REFERENCES projects
(
    id
)
    );

CREATE INDEX IF NOT EXISTS idx_characters_project ON characters(project_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_characters_project_name ON characters(project_id, name);

-- 人物分类
CREATE TABLE IF NOT EXISTS character_categories
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    project_id
    INTEGER
    NOT
    NULL,
    name
    TEXT
    NOT
    NULL,
    description
    TEXT,
    parent_category_ids
    TEXT
    NOT
    NULL
    DEFAULT
    '[]',
    FOREIGN
    KEY
(
    project_id
) REFERENCES projects
(
    id
)
    );

-- 人物分类解析缓存
CREATE TABLE IF NOT EXISTS character_category_resolutions
(
    character_id
    INTEGER
    PRIMARY
    KEY,
    category_ids
    TEXT
    NOT
    NULL
    DEFAULT
    '[]',
    attribute_ids
    TEXT
    NOT
    NULL
    DEFAULT
    '[]',
    FOREIGN
    KEY
(
    character_id
) REFERENCES characters
(
    id
)
    );

-- 人物关系
CREATE TABLE IF NOT EXISTS character_relations
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    project_id
    INTEGER
    NOT
    NULL,
    source_character_id
    INTEGER
    NOT
    NULL,
    target_character_id
    INTEGER
    NOT
    NULL,
    relation_type
    TEXT
    NOT
    NULL,
    description
    TEXT,
    intensity
    REAL,
    created_at
    TEXT
    NOT
    NULL,
    updated_at
    TEXT
    NOT
    NULL,
    FOREIGN
    KEY
(
    project_id
) REFERENCES projects
(
    id
),
    FOREIGN KEY
(
    source_character_id
) REFERENCES characters
(
    id
),
    FOREIGN KEY
(
    target_character_id
) REFERENCES characters
(
    id
)
    );

CREATE INDEX IF NOT EXISTS idx_relations_project ON character_relations(project_id);
CREATE INDEX IF NOT EXISTS idx_relations_source ON character_relations(source_character_id);
CREATE INDEX IF NOT EXISTS idx_relations_target ON character_relations(target_character_id);

-- 人物关系变更日志
CREATE TABLE IF NOT EXISTS character_relation_logs
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    relation_id
    INTEGER
    NOT
    NULL,
    old_type
    TEXT,
    new_type
    TEXT
    NOT
    NULL,
    old_description
    TEXT,
    new_description
    TEXT,
    source
    TEXT
    NOT
    NULL,
    reason
    TEXT
    NOT
    NULL
    DEFAULT
    '',
    scene_id
    INTEGER
    NOT
    NULL,
    created_at
    TEXT
    NOT
    NULL,
    FOREIGN
    KEY
(
    relation_id
) REFERENCES character_relations
(
    id
)
    );

-- 人物场景在场
CREATE TABLE IF NOT EXISTS character_scene_presence
(
    character_id
    INTEGER
    NOT
    NULL,
    scene_id
    INTEGER
    NOT
    NULL,
    PRIMARY
    KEY
(
    character_id,
    scene_id
),
    FOREIGN KEY
(
    character_id
) REFERENCES characters
(
    id
),
    FOREIGN KEY
(
    scene_id
) REFERENCES scenes
(
    id
)
    );

CREATE INDEX IF NOT EXISTS idx_presence_scene ON character_scene_presence(scene_id);

-- 人物状态值
CREATE TABLE IF NOT EXISTS character_state_values
(
    character_id
    INTEGER
    NOT
    NULL,
    attribute_id
    INTEGER
    NOT
    NULL,
    value
    TEXT
    NOT
    NULL
    DEFAULT
    '',
    updated_at
    TEXT
    NOT
    NULL,
    PRIMARY
    KEY
(
    character_id,
    attribute_id
),
    FOREIGN KEY
(
    character_id
) REFERENCES characters
(
    id
),
    FOREIGN KEY
(
    attribute_id
) REFERENCES state_attributes
(
    id
)
    );

-- Schema 版本表
CREATE TABLE IF NOT EXISTS schema_version
(
    version
    INTEGER
    PRIMARY
    KEY,
    applied_at
    TEXT
    NOT
    NULL
);
