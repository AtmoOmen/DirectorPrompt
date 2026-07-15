-- 枚举存储格式从 snake_case 迁移到 PascalCase

-- playthrough_events.type (EventType)
UPDATE playthrough_events
SET type = 'DirectorInput'
WHERE type = 'director_input';
UPDATE playthrough_events
SET type = 'NarrativeOutput'
WHERE type = 'narrative_output';
UPDATE playthrough_events
SET type = 'StateChange'
WHERE type = 'state_change';
UPDATE playthrough_events
SET type = 'MemoryUpdate'
WHERE type = 'memory_update';
UPDATE playthrough_events
SET type = 'CharacterUpdate'
WHERE type = 'character_update';
UPDATE playthrough_events
SET type = 'SceneChange'
WHERE type = 'scene_change';
UPDATE playthrough_events
SET type = 'DirectiveChange'
WHERE type = 'directive_change';
UPDATE playthrough_events
SET type = 'PhaseTransition'
WHERE type = 'phase_transition';

-- state_attributes.scope (StateScope)
UPDATE state_attributes
SET scope = 'Global'
WHERE scope = 'global';
UPDATE state_attributes
SET scope = 'Category'
WHERE scope = 'category';

-- state_attributes.value_type (StateValueType)
UPDATE state_attributes
SET value_type = 'Numeric'
WHERE value_type = 'numeric';
UPDATE state_attributes
SET value_type = 'Enum'
WHERE value_type = 'enum';

-- state_attributes.driver (Driver)
UPDATE state_attributes
SET driver = 'Narrative'
WHERE driver = 'narrative';
UPDATE state_attributes
SET driver = 'System'
WHERE driver = 'system';

-- characters.status (CharacterStatus)
UPDATE characters
SET status = 'Active'
WHERE status = 'active';
UPDATE characters
SET status = 'Archived'
WHERE status = 'archived';

-- scenes.status (SceneStatus)
UPDATE scenes
SET status = 'Active'
WHERE status = 'active';
UPDATE scenes
SET status = 'Completed'
WHERE status = 'completed';
UPDATE scenes
SET status = 'Archived'
WHERE status = 'archived';

-- rounds.status (RoundStatus)
UPDATE rounds
SET status = 'Pending'
WHERE status = 'pending';
UPDATE rounds
SET status = 'Completed'
WHERE status = 'completed';
UPDATE rounds
SET status = 'RolledBack'
WHERE status = 'rolled_back';

-- active_directives.type (DirectiveType)
UPDATE active_directives
SET type = 'Plot'
WHERE type = 'plot';
UPDATE active_directives
SET type = 'Tone'
WHERE type = 'tone';
UPDATE active_directives
SET type = 'TemporaryConstraint'
WHERE type = 'temporary_constraint';
UPDATE active_directives
SET type = 'SceneChange'
WHERE type = 'scene_change';

-- state_change_logs.source (StateChangeSource)
UPDATE state_change_logs
SET source = 'StateAgent'
WHERE source = 'state_agent';
UPDATE state_change_logs
SET source = 'System'
WHERE source = 'system';
UPDATE state_change_logs
SET source = 'DirectorManual'
WHERE source = 'director_manual';

-- character_relation_logs.source (RelationChangeSource)
UPDATE character_relation_logs
SET source = 'MemorySubAgent'
WHERE source = 'memory_sub_agent';
UPDATE character_relation_logs
SET source = 'DirectorManual'
WHERE source = 'director_manual';
