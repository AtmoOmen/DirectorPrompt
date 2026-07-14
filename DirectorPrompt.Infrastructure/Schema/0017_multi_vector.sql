-- 知识条目: 清除 content_hash, 触发多向量重新生成 (关键词各自独立向量 + 正文向量)
UPDATE knowledge_entries
SET content_hash = NULL;

-- 记忆条目: 清除 content_hash, 触发多向量重新生成 (标签各自独立向量 + 正文向量)
UPDATE memory_entries
SET content_hash = NULL;
