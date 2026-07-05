using Dapper;
using DirectorPrompt.Domain.Models;
using DirectorPrompt.Domain.Repositories;

namespace DirectorPrompt.Infrastructure.Repositories;

public sealed class StateSnapshotRepository : IStateSnapshotRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public StateSnapshotRepository(SqliteConnectionFactory connectionFactory) =>
        this.connectionFactory = connectionFactory;

    public async Task<StateSnapshot?> GetLatestAsync
    (
        long              projectID,
        long              beforeRoundID,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<StateSnapshotRow>
                  (
                      """
                      SELECT * FROM state_snapshots
                      WHERE project_id = @projectID AND round_id < @beforeRoundID
                      ORDER BY round_id DESC
                      LIMIT 1
                      """,
                      new { projectID, beforeRoundID }
                  );

        return row?.ToStateSnapshot();
    }

    public async Task<StateSnapshot> CreateAsync(StateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<long>
                 (
                     """
                     INSERT INTO state_snapshots
                         (project_id, round_id, global_state, character_state, flags, active_directives, current_scene_id, scene_characters, created_at)
                     VALUES (@projectID, @roundID, @globalState, @characterState, @flags, @activeDirectives, @currentSceneID, @sceneCharacters, @createdAt);
                     SELECT last_insert_rowid();
                     """,
                     new
                     {
                         projectID        = snapshot.ProjectID,
                         roundID          = snapshot.RoundID,
                         globalState      = snapshot.GlobalState,
                         characterState   = snapshot.CharacterState,
                         flags            = snapshot.Flags,
                         activeDirectives = snapshot.ActiveDirectives,
                         currentSceneID   = snapshot.CurrentSceneID,
                         sceneCharacters  = snapshot.SceneCharacters,
                         createdAt        = snapshot.CreatedAt.ToString("O")
                     }
                 );

        return snapshot with { ID = id };
    }

    public async Task<StateSnapshot?> GetBySceneAsync(long sceneID, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync<StateSnapshotRow>
                  (
                      """
                      SELECT * FROM state_snapshots
                      WHERE current_scene_id = @sceneID
                      ORDER BY round_id DESC
                      LIMIT 1
                      """,
                      new { sceneID }
                  );

        return row?.ToStateSnapshot();
    }

    private sealed class StateSnapshotRow
    {
        public long   ID                { get; set; }
        public long   Project_ID        { get; set; }
        public long   Round_ID          { get; set; }
        public string Global_State      { get; set; } = "{}";
        public string Character_State   { get; set; } = "{}";
        public string Flags             { get; set; } = "{}";
        public string Active_Directives { get; set; } = "{}";
        public long   Current_Scene_ID  { get; set; }
        public string Scene_Characters  { get; set; } = "[]";
        public string Created_At        { get; set; } = string.Empty;

        public StateSnapshot ToStateSnapshot() =>
            new()
            {
                ID               = ID,
                ProjectID        = Project_ID,
                RoundID          = Round_ID,
                GlobalState      = Global_State,
                CharacterState   = Character_State,
                Flags            = Flags,
                ActiveDirectives = Active_Directives,
                CurrentSceneID   = Current_Scene_ID,
                SceneCharacters  = Scene_Characters,
                CreatedAt        = DateTime.Parse(Created_At)
            };
    }
}
