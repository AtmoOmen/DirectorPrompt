-- 移除 Flag 系统: 删除 flags 表和 state_snapshots.flags 列

DROP TABLE IF EXISTS flags;

-- SQLite 不支持 ALTER TABLE DROP COLUMN (需 3.35.0+), 重建 state_snapshots 去掉 flags 列
CREATE TABLE IF NOT EXISTS state_snapshots_new
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
    session_id
    INTEGER,
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

INSERT INTO state_snapshots_new
(id, project_id, session_id, round_id, global_state, character_state, active_directives, current_scene_id,
 scene_characters, created_at)
SELECT id,
       project_id,
       session_id,
       round_id,
       global_state,
       character_state,
       active_directives,
       current_scene_id,
       scene_characters,
       created_at
FROM state_snapshots;

DROP TABLE state_snapshots;
ALTER TABLE state_snapshots_new RENAME TO state_snapshots;

CREATE INDEX IF NOT EXISTS idx_snapshots_project_round ON state_snapshots(project_id, round_id);
CREATE INDEX IF NOT EXISTS idx_snapshots_scene ON state_snapshots(current_scene_id);
CREATE INDEX IF NOT EXISTS idx_snapshots_session ON state_snapshots(session_id);
