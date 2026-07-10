-- playthrough_events: 添加 scene_id 用于场景级历史查询
ALTER TABLE playthrough_events
    ADD COLUMN scene_id INTEGER;
CREATE INDEX IF NOT EXISTS idx_events_scene ON playthrough_events(scene_id);

-- scenes: 添加 progress_summary 用于场景内压缩摘要
ALTER TABLE scenes
    ADD COLUMN progress_summary TEXT;

-- rounds: 添加 session_id 和 status 用于场景级关联和生命周期管理
ALTER TABLE rounds
    ADD COLUMN session_id INTEGER;
ALTER TABLE rounds
    ADD COLUMN status TEXT NOT NULL DEFAULT 'pending';
CREATE INDEX IF NOT EXISTS idx_rounds_session ON rounds(session_id);
CREATE INDEX IF NOT EXISTS idx_rounds_scene ON rounds(scene_id);
