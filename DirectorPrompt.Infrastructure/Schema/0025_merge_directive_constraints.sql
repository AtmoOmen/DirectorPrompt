UPDATE active_directives
SET type = 'Constraint'
WHERE type IN ('Tone', 'TemporaryConstraint');

UPDATE playthrough_events
SET data =
    (
        SELECT json_group_array
               (
                   json
                   (
                       CASE
                           WHEN json_extract(item.value, '$.type') IN ('Tone', 'TemporaryConstraint')
                               THEN json_set(item.value, '$.type', 'Constraint')
                           ELSE item.value
                       END
                   )
               )
        FROM json_each(playthrough_events.data) AS item
    )
WHERE type = 'DirectorInput'
  AND json_valid(data)
  AND json_type(data) = 'array'
  AND EXISTS
    (
        SELECT 1
        FROM json_each(playthrough_events.data) AS item
        WHERE json_extract(item.value, '$.type') IN ('Tone', 'TemporaryConstraint')
    );

UPDATE state_attributes
SET config = json_set
             (
                 config,
                 '$.phases',
                 json
                 (
                     (
                         SELECT json_group_array
                                (
                                    json
                                    (
                                        json_set
                                        (
                                            phase.value,
                                            '$.enterDirectives',
                                            json
                                            (
                                                (
                                                    SELECT json_group_array
                                                           (
                                                               json
                                                               (
                                                                   CASE
                                                                       WHEN json_extract(directive.value, '$.type') IN ('Tone', 'TemporaryConstraint')
                                                                           THEN json_set(directive.value, '$.type', 'Constraint')
                                                                       ELSE directive.value
                                                                   END
                                                               )
                                                           )
                                                    FROM json_each(phase.value, '$.enterDirectives') AS directive
                                                )
                                            ),
                                            '$.exitDirectives',
                                            json
                                            (
                                                (
                                                    SELECT json_group_array
                                                           (
                                                               json
                                                               (
                                                                   CASE
                                                                       WHEN json_extract(directive.value, '$.type') IN ('Tone', 'TemporaryConstraint')
                                                                           THEN json_set(directive.value, '$.type', 'Constraint')
                                                                       ELSE directive.value
                                                                   END
                                                               )
                                                           )
                                                    FROM json_each(phase.value, '$.exitDirectives') AS directive
                                                )
                                            )
                                        )
                                    )
                                )
                         FROM json_each(state_attributes.config, '$.phases') AS phase
                     )
                 )
             )
WHERE json_valid(config)
  AND json_type(config, '$.phases') = 'array'
  AND EXISTS
    (
        SELECT 1
        FROM json_each(state_attributes.config, '$.phases') AS phase
        WHERE EXISTS
              (
                  SELECT 1
                  FROM json_each(phase.value, '$.enterDirectives') AS directive
                  WHERE json_extract(directive.value, '$.type') IN ('Tone', 'TemporaryConstraint')
              )
           OR EXISTS
              (
                  SELECT 1
                  FROM json_each(phase.value, '$.exitDirectives') AS directive
                  WHERE json_extract(directive.value, '$.type') IN ('Tone', 'TemporaryConstraint')
              )
    );

UPDATE round_changes
SET old_data = json_set(old_data, '$.type', 'Constraint')
WHERE table_name = 'active_directives'
  AND json_valid(old_data)
  AND json_extract(old_data, '$.type') IN ('Tone', 'TemporaryConstraint');
