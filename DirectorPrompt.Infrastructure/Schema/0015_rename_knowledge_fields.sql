-- knowledge_entries: 重命名 title → remarks, tags → keywords
ALTER TABLE knowledge_entries
    RENAME COLUMN title TO remarks;

ALTER TABLE knowledge_entries
    RENAME COLUMN tags TO keywords;
