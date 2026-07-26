CREATE TABLE IF NOT EXISTS state_rule_executions
(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id INTEGER NOT NULL,
    round_id INTEGER NOT NULL,
    scene_id INTEGER NOT NULL DEFAULT 0,
    attribute_id INTEGER NOT NULL,
    character_id INTEGER NOT NULL DEFAULT 0,
    rule_id TEXT NOT NULL,
    event_key TEXT NOT NULL,
    condition_met INTEGER NOT NULL,
    fired INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    UNIQUE(session_id, attribute_id, character_id, rule_id, event_key)
);

CREATE INDEX IF NOT EXISTS idx_state_rule_executions_lookup
ON state_rule_executions(session_id, attribute_id, character_id, rule_id, id);

CREATE INDEX IF NOT EXISTS idx_state_rule_executions_round
ON state_rule_executions(session_id, round_id);
