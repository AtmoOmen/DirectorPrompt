-- 回滚日志: 记录每轮中所有表的数据变更, 供回滚使用

CREATE TABLE IF NOT EXISTS round_changes
(
    id
    INTEGER
    PRIMARY
    KEY
    AUTOINCREMENT,
    round_id
    INTEGER
    NOT
    NULL,
    table_name
    TEXT
    NOT
    NULL,
    record_id
    INTEGER
    NOT
    NULL,
    operation
    TEXT
    NOT
    NULL,
    old_data
    TEXT,
    created_at
    TEXT
    NOT
    NULL
);

CREATE INDEX IF NOT EXISTS idx_round_changes_round ON round_changes(round_id);
