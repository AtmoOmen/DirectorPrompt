-- characters: 添加进入/退出指令
ALTER TABLE characters
    ADD COLUMN enter_directives TEXT NOT NULL DEFAULT '[]';
ALTER TABLE characters
    ADD COLUMN exit_directives TEXT NOT NULL DEFAULT '[]';
