ALTER TABLE scenes
    ADD COLUMN progress_summary_round_id INTEGER NOT NULL DEFAULT 0;

ALTER TABLE knowledge_entries
    ADD COLUMN embedding_fingerprint TEXT;

ALTER TABLE memory_entries
    ADD COLUMN embedding_fingerprint TEXT;

UPDATE knowledge_entries
SET content_hash          = NULL,
    embedding_fingerprint = NULL;

UPDATE memory_entries
SET content_hash          = NULL,
    embedding_fingerprint = NULL;

CREATE INDEX IF NOT EXISTS idx_events_session_id_desc
    ON playthrough_events(session_id, id DESC);

CREATE INDEX IF NOT EXISTS idx_events_session_scene_round
    ON playthrough_events(session_id, scene_id, round_id, id DESC);

CREATE INDEX IF NOT EXISTS idx_events_session_type_round
    ON playthrough_events(session_id, type, round_id DESC, id DESC);

CREATE INDEX IF NOT EXISTS idx_memory_session_timeline_id
    ON memory_entries(session_id, timeline_pos DESC, id DESC);

CREATE INDEX IF NOT EXISTS idx_characters_session_status_touch
    ON characters(session_id, status, last_touched_round DESC, id);

CREATE INDEX IF NOT EXISTS idx_relations_session_source_target
    ON character_relations(session_id, source_character_id, target_character_id);

CREATE
VIRTUAL TABLE IF NOT EXISTS memory_search USING fts5
(
    content,
    tags,
    content='memory_entries',
    content_rowid='id',
    tokenize='trigram'
);

CREATE TRIGGER IF NOT EXISTS memory_search_insert AFTER INSERT ON memory_entries
BEGIN
    INSERT INTO memory_search(rowid, content, tags)
    VALUES (new.id, new.content, new.tags);
END;

CREATE TRIGGER IF NOT EXISTS memory_search_delete AFTER
DELETE
ON memory_entries BEGIN
    INSERT INTO memory_search(memory_search, rowid, content, tags)
    VALUES ('delete', old.id, old.content, old.tags);
END;

CREATE TRIGGER IF NOT EXISTS memory_search_update AFTER
UPDATE OF content, tags
ON memory_entries BEGIN
INSERT
INTO memory_search(memory_search, rowid, content, tags)
VALUES ('delete', old.id, old.content, old.tags);
INSERT INTO memory_search(rowid, content, tags)
VALUES (new.id, new.content, new.tags);
END;

INSERT INTO memory_search(memory_search)
VALUES ('rebuild');
