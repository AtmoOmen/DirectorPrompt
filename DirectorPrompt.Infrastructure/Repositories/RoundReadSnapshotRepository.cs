using Dapper;
using DirectorPrompt.Domain.Enums;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;
using StateDriver = DirectorPrompt.Domain.Enums.Driver;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class RoundReadSnapshotRepository
(
    SqliteDatabaseScheduler scheduler
) : IRoundReadSnapshotRepository
{
    public Task<RoundReadSnapshot> GetAsync
    (
        long              projectID,
        long              sessionID,
        long?             sceneID,
        CancellationToken cancellationToken = default
    ) =>
        scheduler.ExecuteAsync
        (
            async (connection, token) =>
            {
                await using var transaction = await connection.BeginTransactionAsync(token);
                var scene = sceneID is null ?
                                null :
                                await connection.QueryFirstOrDefaultAsync<SceneRow>
                                (
                                    new CommandDefinition
                                    (
                                        "SELECT * FROM scenes WHERE id = @sceneID AND session_id = @sessionID",
                                        new { sceneID, sessionID },
                                        transaction,
                                        cancellationToken: token
                                    )
                                );
                var attributeRows = await connection.QueryAsync<StateAttributeRow>
                                    (
                                        new CommandDefinition
                                        (
                                            "SELECT * FROM state_attributes WHERE project_id = @projectID AND scope IN ('global', 'category') ORDER BY id",
                                            new { projectID },
                                            transaction,
                                            cancellationToken: token
                                        )
                                    );
                var attributes = attributeRows.Select(row => row.ToStateAttribute()).ToList();
                var globalValues = await connection.QueryAsync<StateValueRow>
                                   (
                                       new CommandDefinition
                                       (
                                           """
                                           SELECT sv.*
                                           FROM state_values sv
                                           JOIN state_attributes sa ON sa.id = sv.attribute_id
                                           WHERE sa.project_id = @projectID
                                             AND sa.scope = 'global'
                                             AND sv.session_id = @sessionID
                                           ORDER BY sv.attribute_id
                                           """,
                                           new { projectID, sessionID },
                                           transaction,
                                           cancellationToken: token
                                       )
                                   );
                var directives = await connection.QueryAsync<ActiveDirectiveRow>
                                 (
                                     new CommandDefinition
                                     (
                                         """
                                         SELECT *
                                         FROM active_directives
                                         WHERE session_id = @sessionID
                                           AND (ttl IS NULL OR ttl > 0)
                                         ORDER BY id
                                         """,
                                         new { sessionID },
                                         transaction,
                                         cancellationToken: token
                                     )
                                 );
                IReadOnlyList<Character>           characters      = [];
                IReadOnlyList<CharacterStateValue> characterValues = [];
                IReadOnlyList<CharacterRelation>   relations       = [];

                if (sceneID is not null)
                {
                    var characterRows = await connection.QueryAsync<CharacterRow>
                                        (
                                            new CommandDefinition
                                            (
                                                """
                                                SELECT c.*
                                                FROM characters c
                                                JOIN character_scene_presence p ON p.character_id = c.id
                                                WHERE p.scene_id = @sceneID
                                                  AND c.session_id = @sessionID
                                                  AND c.status = 'active'
                                                ORDER BY c.id
                                                """,
                                                new { sceneID, sessionID },
                                                transaction,
                                                cancellationToken: token
                                            )
                                        );
                    characters = characterRows.Select(row => row.ToCharacter()).ToList();
                    var characterIDs = characters.Select(character => character.ID).ToArray();

                    if (characterIDs.Length > 0)
                    {
                        var valueRows = await connection.QueryAsync<CharacterStateValueRow>
                                        (
                                            new CommandDefinition
                                            (
                                                "SELECT * FROM character_state_values WHERE character_id IN @characterIDs",
                                                new { characterIDs },
                                                transaction,
                                                cancellationToken: token
                                            )
                                        );
                        characterValues = valueRows.Select(row => row.ToCharacterStateValue()).ToList();
                        var relationRows = await connection.QueryAsync<CharacterRelationRow>
                                           (
                                               new CommandDefinition
                                               (
                                                   """
                                                   SELECT *
                                                   FROM character_relations
                                                   WHERE session_id = @sessionID
                                                     AND
                                                     (
                                                         source_character_id IN @characterIDs
                                                         OR target_character_id IN @characterIDs
                                                     )
                                                   ORDER BY id
                                                   """,
                                                   new { sessionID, characterIDs },
                                                   transaction,
                                                   cancellationToken: token
                                               )
                                           );
                        relations = relationRows.Select(row => row.ToCharacterRelation()).ToList();
                    }
                }

                await transaction.CommitAsync(token);

                return new RoundReadSnapshot
                {
                    Scene               = scene?.ToScene(),
                    GlobalAttributes    = attributes.Where(attribute => attribute.Scope == StateScope.Global).ToList(),
                    GlobalValues        = globalValues.Select(row => row.ToStateValue()).ToList(),
                    ActiveDirectives    = directives.Select(row => row.ToActiveDirective()).ToList(),
                    SceneCharacters     = characters,
                    CharacterAttributes = attributes.Where(attribute => attribute.Scope == StateScope.Category).ToList(),
                    CharacterValues     = characterValues,
                    CharacterRelations  = relations
                };
            },
            cancellationToken: cancellationToken
        );

    private sealed class SceneRow
    {
        public long    ID                        { get; set; }
        public long    Project_ID                { get; set; }
        public long?   Session_ID                { get; set; }
        public long    Timeline_Position         { get; set; }
        public string  Time_Label                { get; set; } = string.Empty;
        public string? Summary                   { get; set; }
        public string? Progress_Summary          { get; set; }
        public long    Progress_Summary_Round_ID { get; set; }
        public string  Status                    { get; set; } = "active";

        public Scene ToScene() =>
            new()
            {
                ID                     = ID,
                ProjectID              = Project_ID,
                SessionID              = Session_ID ?? 0,
                TimelinePosition       = Timeline_Position,
                TimeLabel              = Time_Label,
                Summary                = Summary,
                ProgressSummary        = Progress_Summary,
                ProgressSummaryRoundID = Progress_Summary_Round_ID,
                Status                 = Status == "completed" ? SceneStatus.Completed : Status == "archived" ? SceneStatus.Archived : SceneStatus.Active
            };
    }

    private sealed class StateAttributeRow
    {
        public long   ID           { get; set; }
        public long   Project_ID   { get; set; }
        public string Name         { get; set; } = string.Empty;
        public string Display_Name { get; set; } = string.Empty;
        public string Scope        { get; set; } = "global";
        public long?  Category_ID  { get; set; }
        public string Value_Type   { get; set; } = "numeric";
        public string Driver       { get; set; } = "narrative";
        public string Config       { get; set; } = "{}";

        public StateAttribute ToStateAttribute() =>
            new()
            {
                ID          = ID,
                ProjectID   = Project_ID,
                Name        = Name,
                DisplayName = Display_Name,
                Scope = Scope == "category" ?
                            StateScope.Category :
                            StateScope.Global,
                CategoryID = Category_ID,
                ValueType = Value_Type == "enum" ?
                                StateValueType.Enum :
                                StateValueType.Numeric,
                Driver = Driver == "system" ?
                             StateDriver.System :
                             StateDriver.Narrative,
                Config = Config
            };
    }

    private sealed class StateValueRow
    {
        public long   Attribute_ID { get; set; }
        public string Value        { get; set; } = string.Empty;
        public string Updated_At   { get; set; } = string.Empty;

        public StateValue ToStateValue() =>
            new()
            {
                AttributeID = Attribute_ID,
                Value       = Value,
                UpdatedAt   = DateTime.Parse(Updated_At)
            };
    }

    private sealed class ActiveDirectiveRow
    {
        public long   ID         { get; set; }
        public long   Project_ID { get; set; }
        public long?  Session_ID { get; set; }
        public string Type       { get; set; } = "plot";
        public string Content    { get; set; } = string.Empty;
        public int?   TTL        { get; set; }
        public string Created_At { get; set; } = string.Empty;

        public ActiveDirective ToActiveDirective() =>
            new()
            {
                ID        = ID,
                ProjectID = Project_ID,
                SessionID = Session_ID ?? 0,
                Type = Type switch
                {
                    "tone"                 => DirectiveType.Tone,
                    "temporary_constraint" => DirectiveType.TemporaryConstraint,
                    "scene_change"         => DirectiveType.SceneChange,
                    _                      => DirectiveType.Plot
                },
                Content   = Content,
                TTL       = TTL,
                CreatedAt = DateTime.Parse(Created_At)
            };
    }

    private sealed class CharacterRow
    {
        public long    ID                 { get; set; }
        public long    Project_ID         { get; set; }
        public long    Session_ID         { get; set; }
        public string  Name               { get; set; } = string.Empty;
        public string  Description        { get; set; } = string.Empty;
        public string  Aliases            { get; set; } = "[]";
        public string  Category_IDs       { get; set; } = "[]";
        public string  Status             { get; set; } = "active";
        public int     Touch_Count        { get; set; }
        public long    Last_Touched_Round { get; set; }
        public string? Content_Hash       { get; set; }
        public string  Created_At         { get; set; } = string.Empty;
        public string  Updated_At         { get; set; } = string.Empty;

        public Character ToCharacter() =>
            new()
            {
                ID          = ID,
                ProjectID   = Project_ID,
                SessionID   = Session_ID,
                Name        = Name,
                Description = Description,
                Aliases     = JsonHelper.DeserializeStringArray(Aliases),
                CategoryIDs = JsonHelper.DeserializeInt64Array(Category_IDs),
                Status = Status == "archived" ?
                             CharacterStatus.Archived :
                             CharacterStatus.Active,
                TouchCount       = Touch_Count,
                LastTouchedRound = Last_Touched_Round,
                ContentHash      = Content_Hash,
                CreatedAt        = DateTime.Parse(Created_At),
                UpdatedAt        = DateTime.Parse(Updated_At)
            };
    }

    private sealed class CharacterStateValueRow
    {
        public long   Character_ID { get; set; }
        public long   Attribute_ID { get; set; }
        public string Value        { get; set; } = string.Empty;
        public string Updated_At   { get; set; } = string.Empty;

        public CharacterStateValue ToCharacterStateValue() =>
            new()
            {
                CharacterID = Character_ID,
                AttributeID = Attribute_ID,
                Value       = Value,
                UpdatedAt   = DateTime.Parse(Updated_At)
            };
    }

    private sealed class CharacterRelationRow
    {
        public long    ID                  { get; set; }
        public long    Project_ID          { get; set; }
        public long    Session_ID          { get; set; }
        public long    Source_Character_ID { get; set; }
        public long    Target_Character_ID { get; set; }
        public string  Relation_Type       { get; set; } = string.Empty;
        public string? Description         { get; set; }
        public float?  Intensity           { get; set; }
        public string  Created_At          { get; set; } = string.Empty;
        public string  Updated_At          { get; set; } = string.Empty;

        public CharacterRelation ToCharacterRelation() =>
            new()
            {
                ID                = ID,
                ProjectID         = Project_ID,
                SessionID         = Session_ID,
                SourceCharacterID = Source_Character_ID,
                TargetCharacterID = Target_Character_ID,
                RelationType      = Relation_Type,
                Description       = Description,
                Intensity         = Intensity,
                CreatedAt         = DateTime.Parse(Created_At),
                UpdatedAt         = DateTime.Parse(Updated_At)
            };
    }
}
