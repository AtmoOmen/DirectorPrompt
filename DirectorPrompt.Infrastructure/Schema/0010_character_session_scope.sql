-- 人物改为对话级作用域: session_id NOT NULL, 唯一索引改为 (session_id, name)

-- 删除残留的项目模板角色 (session_id IS NULL), 这些不应该存在
DELETE
FROM characters
WHERE session_id IS NULL;

-- 删除旧的唯一索引 (project_id, name), 该索引错误地限制了同一项目内跨对话的角色名唯一性
DROP INDEX IF EXISTS idx_characters_project_name;

-- 重建 characters 表, session_id 改为 NOT NULL
CREATE TABLE IF NOT EXISTS characters_new
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
),
    FOREIGN KEY
(
    session_id
) REFERENCES sessions
(
    id
)
    );

INSERT INTO characters_new (id, project_id, session_id, name, description, category_ids, status, created_at, updated_at)
SELECT id,
       project_id,
       session_id,
       name,
       description,
       category_ids,
       status,
       created_at,
       updated_at
FROM characters;

DROP TABLE characters;
ALTER TABLE characters_new RENAME TO characters;

CREATE INDEX IF NOT EXISTS idx_characters_project ON characters(project_id);
CREATE INDEX IF NOT EXISTS idx_characters_session ON characters(session_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_characters_session_name ON characters(session_id, name);
