-- 记忆配置和知识检索配置从项目级移至全局, 移除项目表中的对应列
ALTER TABLE projects DROP COLUMN memory_config;
ALTER TABLE projects DROP COLUMN knowledge_config;
