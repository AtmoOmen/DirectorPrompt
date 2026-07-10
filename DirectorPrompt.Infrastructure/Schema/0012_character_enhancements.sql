-- 角色增强: 别称, 触及计数, 最近触及轮次, 内容哈希
ALTER TABLE characters
    ADD COLUMN aliases TEXT NOT NULL DEFAULT '[]';
ALTER TABLE characters
    ADD COLUMN touch_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE characters
    ADD COLUMN last_touched_round INTEGER NOT NULL DEFAULT 0;
ALTER TABLE characters
    ADD COLUMN content_hash TEXT;

-- 迁移旧状态: left/dead → active (退场信息由记忆系统承载)
UPDATE characters
SET status = 'active'
WHERE status IN ('left', 'dead');

-- 已有角色的 last_touched_round 初始化为当前最大轮次, 避免被 ArchiveStaleAsync 立即归档
UPDATE characters
SET last_touched_round = COALESCE((SELECT MAX(id) FROM rounds), 0);
