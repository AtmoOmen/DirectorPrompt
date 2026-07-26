UPDATE state_attributes
SET config = replace
             (
                 replace
                 (
                     replace(config, '"DirectorDirective"', '"UserInput"'),
                     '"DirectorCommand"',
                     '"InputContent"'
                 ),
                 '"DirectiveType"',
                 '"InputType"'
             )
WHERE json_valid(config)
  AND
    (
        instr(config, '"DirectorDirective"') > 0
        OR instr(config, '"DirectorCommand"') > 0
        OR instr(config, '"DirectiveType"') > 0
    );
