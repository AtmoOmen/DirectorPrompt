-- 知识条目: 添加 content_hash 列 (向量存储在 vec0 虚拟表中, 不在主表)
ALTER TABLE knowledge_entries
    ADD COLUMN content_hash TEXT;

-- 记忆条目: 添加 content_hash 列
ALTER TABLE memory_entries
    ADD COLUMN content_hash TEXT;

-- 向量表元数据: 记录每个项目级 vec0 虚拟表的维度信息
CREATE TABLE IF NOT EXISTS vector_tables
(
    table_name
    TEXT
    PRIMARY
    KEY,
    dimension
    INTEGER
    NOT
    NULL,
    created_at
    TEXT
    NOT
    NULL
);
