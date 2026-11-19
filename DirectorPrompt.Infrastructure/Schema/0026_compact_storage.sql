DELETE
FROM round_changes
WHERE NOT EXISTS (SELECT 1 FROM sessions WHERE sessions.id = round_changes.session_id);

CREATE TRIGGER knowledge_entries_insert_index
    AFTER INSERT
    ON knowledge_entries
    WHEN new.content_hash IS NOT NULL
BEGIN
    UPDATE knowledge_entries SET content_hash = NULL WHERE id = new.id;
END;

CREATE TRIGGER memory_entries_insert_index
    AFTER INSERT
    ON memory_entries
    WHEN new.content_hash IS NOT NULL
BEGIN
    UPDATE memory_entries SET content_hash = NULL WHERE id = new.id;
END;

CREATE TRIGGER characters_insert_index
    AFTER INSERT
    ON characters
    WHEN new.content_hash IS NOT NULL
BEGIN
    UPDATE characters SET content_hash = NULL WHERE id = new.id;
END;
