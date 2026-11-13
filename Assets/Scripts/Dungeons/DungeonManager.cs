using System.Text.RegularExpressions;
using LevelGeneration;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs the dungeon of a scene whose level layout is a Dungeon (GameArea): once the level is built it counts the enemies
/// the level's spawners will make, then works through the DungeonDefinition's steps in order, reading kills from the
/// DungeonRun's DungeonKill buffer (EnemyKillSystem). Every kill rolls the kill loot table onto the ground where the
/// enemy died (GroundLootManager). After the last step the return portal opens next to the player, the completion loot
/// table is rolled onto the ground in front of it, and the finish is saved on the character. A Survive step sends waves
/// at the player (they always know where it is). Leave goes back without finishing; the next visit is a new run with a
/// new layout (every scene load makes a new world). Kept between scenes (GameBootstrap's rig); DungeonHUD shows it.
///
/// The dungeon's TimeLimit runs out (Fail, no further step starts) unless it's finished first; its clock stops in
/// Survive steps, which have their own. What a death of any player (this machine's or another's) does depends on the
/// step: in Kill steps it takes DeathPenalty × (1 + the run's earlier deaths) seconds off the clock, in Reach Exit
/// steps the player respawns at the level's start (RespawnAtStart), and in Survive steps it fails the dungeon.
///
/// Only the host runs the dungeon; it sends how the run stands to the clients (DungeonStatusRpc, ApplyStatus), so their
/// DungeonHUD shows it too.
/// </summary>
public class DungeonManager : MonoBehaviour
{
    public enum RunState
    {
        None,       // Not in a dungeon (or it isn't loaded yet)
        Running,
        Complete,
        Failed,     // Died in a Survive step, or out of time: the enemies are gone and only the return portal is left
    }

    public static DungeonManager Instance;

    [Tooltip("Meters from the player where the return portal opens")]
    public float ReturnPortalDistance = 4f;
    [Tooltip("Meters from the player where a boss spawns when the level has no Boss Spawn Point built")]
    public float BossDistance = 10f;
    [Tooltip("Seconds a death in a Kill step takes off the time limit, times (1 + the run's earlier deaths)")]
    public float DeathPenalty = 10f;

    // Seconds between status messages to the clients while nothing else changed (their clock runs on in between)
    const float StatusInterval = 1f;

    // Directions tried around the player for the boss and the return portal before settling for one without ground
    const int GroundTries = 8;

    World _world;
    bool _checked;              // The world's area was read (a town, or a dungeon that started)
    Entity _run;
    Entity _returnPortalPrefab;
    string _returnScene;
    bool _portalPending;        // Complete, but the player wasn't there to open the portal (and drop the loot) next to

    DungeonDefinition _dungeon;
    RunState _state;
    int _step;
    int _kills;                 // Non-boss enemies killed since the run started
    int _totalEnemies;          // In the level's spawners
    bool _bossSpawned;
    bool _bossKilled;
    Entity _boss;
    float _survived;            // Seconds into the current Survive step
    float _nextWave;            // Seconds until its next wave
    float _timeLeft;            // Of the dungeon's TimeLimit
    bool _outOfTime;            // Failed because the time ran out
    int _deaths;                // Players' deaths this run, all players together
    int _newDeaths;             // Since the last Update (CharacterManager.PlayerDied, NetPlayerSpawnSystem.RemotePlayerDied)
    float _statusTimer;         // Until the next status message to the clients
    DungeonStatusRpc _sentStatus;

    public RunState State => _state;
    public DungeonDefinition Dungeon => _dungeon;
    public int StepIndex => _step;
    public int StepCount => _dungeon != null ? _dungeon.Steps.Count : 0;
    public int Kills => _kills;
    public int TotalEnemies => _totalEnemies;
    public int Deaths => _deaths;
    public bool OutOfTime => _outOfTime;

    /// <summary>The step being worked on, or null when not running.</summary>
    public DungeonStep CurrentStep =>
        _state == RunState.Running && _dungeon != null && _step < _dungeon.Steps.Count ? _dungeon.Steps[_step] : null;

    /// <summary>Seconds left of the current Survive step.</summary>
    public float SurviveTimeLeft => CurrentStep is { Kind: DungeonStepKind.Survive } step ? Mathf.Max(0f, step.SurviveSeconds - _survived) : 0f;

    /// <summary>The dungeon has a time limit.</summary>
    public bool HasTimeLimit => _dungeon != null && _dungeon.TimeLimit > 0f;

    /// <summary>Seconds left of the time limit.</summary>
    public float TimeLeft => Mathf.Max(0f, _timeLeft);

    /// <summary>The time limit's clock runs now: running, and not in a Survive step (it has its own).</summary>
    public bool ClockRunning => HasTimeLimit && CurrentStep is { } step && step.Kind != DungeonStepKind.Survive;

    /// <summary>Seconds the next death takes off the clock (0 when deaths cost no time in this step).</summary>
    public float NextDeathPenalty =>
        ClockRunning && CurrentStep.Kind is DungeonStepKind.KillEnemies or DungeonStepKind.KillBoss ? DeathPenalty * (1 + _deaths) : 0f;

    /// <summary>Players respawn at the level's start rather than next to another player: in a Reach Exit step.</summary>
    public static bool RespawnAtStart => Instance != null && Instance.CurrentStep is { Kind: DungeonStepKind.ReachExit };

    /// <summary>Kills a Kill Enemies step needs (all of the level's spawned enemies times its percent, rounded up).</summary>
    public int KillTarget(DungeonStep step) =>
        step == null ? 0 : Mathf.Min(_totalEnemies, Mathf.CeilToInt(_totalEnemies * step.KillPercent / 100f - 0.001f));

    /// <summary>Where Leave and the return portal go: the scene the dungeon was entered from, else the town.</summary>
    public string ReturnScene => _returnScene ?? FindReturnScene();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // This machine's players, and the ones other machines play here
        CharacterManager.PlayerDied += OnPlayerDied;
        NetPlayerSpawnSystem.RemotePlayerDied += OnPlayerDied;
    }

    void OnDestroy()
    {
        CharacterManager.PlayerDied -= OnPlayerDied;
        NetPlayerSpawnSystem.RemotePlayerDied -= OnPlayerDied;
        if (Instance == this) Instance = null;
    }

    void OnPlayerDied() => _newDeaths++;

    void Update()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != _world)
        {
            _world = world;
            ResetRun();
        }
        if (world == null || !world.IsCreated) return;
        // In someone else's game the host runs the dungeon (its steps, spawns, completion and return portal); between its
        // status messages the clocks run on here
        if (!NetSession.IsHost)
        {
            _newDeaths = 0;
            if (ClockRunning) _timeLeft -= Time.deltaTime;
            if (CurrentStep is { Kind: DungeonStepKind.Survive }) _survived += Time.deltaTime;
            return;
        }
        var entityManager = world.EntityManager;

        if (!_checked)
        {
            TryStart(entityManager);
            return;
        }
        if (_state == RunState.None) return;

        ReadKills(entityManager);
        if (_state == RunState.Running) UpdateSteps(entityManager);
        _newDeaths = 0;
        if (_portalPending) OpenReturnPortal(entityManager);
        SendStatus(entityManager);
    }

    // To the clients: when something they show changed, and every StatusInterval
    void SendStatus(EntityManager entityManager)
    {
        var status = new DungeonStatusRpc
        {
            State = (byte)_state, Step = (byte)_step, Kills = _kills, TotalEnemies = _totalEnemies, Deaths = _deaths,
            OutOfTime = _outOfTime, TimeLeft = _timeLeft, Survived = _survived,
        };
        _statusTimer -= Time.deltaTime;
        bool changed = status.State != _sentStatus.State || status.Step != _sentStatus.Step || status.Kills != _sentStatus.Kills ||
                       status.TotalEnemies != _sentStatus.TotalEnemies || status.Deaths != _sentStatus.Deaths;
        if (!changed && _statusTimer > 0f) return;
        _statusTimer = StatusInterval;
        _sentStatus = status;
        NetPortal.Send(entityManager, status);
    }

    /// <summary>In someone else's game: how the host's run stands (NetDungeonReceiveSystem).</summary>
    public void ApplyStatus(DungeonStatusRpc status)
    {
        if (NetSession.IsHost || !GameArea.TryGet(out var area) || area.Kind != AreaKind.Dungeon || area.Dungeon.Value == null) return;
        _checked = true;
        _dungeon = area.Dungeon.Value;
        _state = (RunState)status.State;
        _step = status.Step;
        _kills = status.Kills;
        _totalEnemies = status.TotalEnemies;
        _deaths = status.Deaths;
        _outOfTime = status.OutOfTime;
        _timeLeft = status.TimeLeft;
        _survived = status.Survived;
    }

    /// <summary>Back to ReturnScene; an unfinished run is lost.</summary>
    public void Leave()
    {
        if (_state == RunState.None) return;
        GameSceneLoader.Load(ReturnScene);
    }

    void ResetRun()
    {
        _checked = false;
        _run = Entity.Null;
        _returnPortalPrefab = Entity.Null;
        _returnScene = null;
        _portalPending = false;
        _dungeon = null;
        _state = RunState.None;
        _step = 0;
        _kills = 0;
        _totalEnemies = 0;
        _timeLeft = 0f;
        _outOfTime = false;
        _deaths = 0;
        _newDeaths = 0;
        _statusTimer = 0f;
        _sentStatus = default;
        ResetStep();
    }

    void ResetStep()
    {
        _bossSpawned = false;
        _bossKilled = false;
        _boss = Entity.Null;
        _survived = 0f;
        _nextWave = 0f;
    }

    // Once the level is built: every spawner exists, so the run's enemy count is known
    void TryStart(EntityManager entityManager)
    {
        if (!GameArea.TryGet(out var area)) return;
        if (area.Kind != AreaKind.Dungeon)
        {
            _checked = true;
            return;
        }
        if (!LevelState.IsReady(entityManager)) return;
        _checked = true;

        _dungeon = area.Dungeon.Value;
        if (_dungeon == null)
        {
            Debug.LogWarning("DungeonManager: this level layout is a Dungeon without a Dungeon, so it has no steps.", this);
            return;
        }

        using var spawners = entityManager.CreateEntityQuery(ComponentType.ReadOnly<LevelEnemySpawner>(), ComponentType.ReadOnly<LevelEnemySpawnerEntry>());
        using var spawnerEntities = spawners.ToEntityArray(Allocator.Temp);
        foreach (var spawner in spawnerEntities)
            foreach (var entry in entityManager.GetBuffer<LevelEnemySpawnerEntry>(spawner, isReadOnly: true))
                _totalEnemies += math.max(0, entry.Count);

        _run = entityManager.CreateEntity(typeof(DungeonRun));
        entityManager.AddBuffer<DungeonKill>(_run);
        entityManager.SetName(_run, "DungeonRun");
        _returnPortalPrefab = area.ReturnPortal;
        _returnScene = FindReturnScene();
        _state = RunState.Running;
        _step = 0;
        _timeLeft = _dungeon.TimeLimit;
        _deaths = 0;
        _newDeaths = 0;
        ResetStep();
        Debug.Log($"DungeonManager: {_dungeon.DisplayName} started, {_totalEnemies} enemies in its spawners, {_dungeon.Steps.Count} steps, " +
                  $"{(HasTimeLimit ? $"{_dungeon.TimeLimit:0} s" : "no")} time limit. Returns to {_returnScene}.", this);
    }

    static string FindReturnScene()
    {
        string previous = GameSceneLoader.PreviousScene;
        bool cameFromGame = !string.IsNullOrEmpty(previous) && previous != SceneManager.GetActiveScene().name && !GameBootstrap.IsMenuScene(previous);
        return cameFromGame ? previous : GameBootstrap.TownScene;
    }

    void ReadKills(EntityManager entityManager)
    {
        if (_run == Entity.Null || !entityManager.Exists(_run)) return;
        var buffer = entityManager.GetBuffer<DungeonKill>(_run);
        if (buffer.IsEmpty) return;
        using var kills = buffer.ToNativeArray(Allocator.Temp);
        buffer.Clear();
        // Every kill rolls the kill table; the items land where the enemy died
        foreach (var kill in kills)
        {
            if (kill.Boss) _bossKilled = true;
            else _kills++;
            DropLoot(entityManager, NetLootTable.Kill, kill.Position);
        }
    }

    // Personal loot: this machine's drop from the dungeon's table, and every other machine in the game rolls its own
    void DropLoot(EntityManager entityManager, NetLootTable table, float3 position)
    {
        var groundLoot = GroundLootManager.Instance;
        var lootTable = table == NetLootTable.Kill ? _dungeon.KillLootTable : _dungeon.CompletionLootTable;
        if (groundLoot != null) groundLoot.DropFromTable(lootTable, position);
        NetLoot.Send(entityManager, table, position);
    }

    void UpdateSteps(EntityManager entityManager)
    {
        // Deaths: in a Survive step one fails the dungeon; in Kill steps each takes time off the clock (more for every
        // earlier one); in Reach Exit steps the player respawns at the start (RespawnAtStart)
        for (int i = 0; i < _newDeaths; i++)
        {
            if (CurrentStep is { Kind: DungeonStepKind.Survive })
            {
                Fail(entityManager, "a player died in a Survive step");
                return;
            }
            float penalty = NextDeathPenalty;
            _timeLeft -= penalty;
            _deaths++;
            if (penalty > 0f) Debug.Log($"DungeonManager: death {_deaths} costs {penalty:0} s, {TimeLeft:0} s left.", this);
        }

        // Out of time: no further step starts
        if (ClockRunning)
        {
            _timeLeft -= Time.deltaTime;
            if (_timeLeft <= 0f)
            {
                _timeLeft = 0f;
                _outOfTime = true;
                Fail(entityManager, "out of time");
                return;
            }
        }

        while (_state == RunState.Running)
        {
            if (_step >= _dungeon.Steps.Count)
            {
                Complete(entityManager);
                return;
            }
            if (!IsStepDone(entityManager, _dungeon.Steps[_step])) return;
            _step++;
            ResetStep();
        }
    }

    bool IsStepDone(EntityManager entityManager, DungeonStep step)
    {
        if (step == null) return true;
        switch (step.Kind)
        {
            case DungeonStepKind.KillEnemies:
                return _kills >= KillTarget(step);

            case DungeonStepKind.KillBoss:
                if (step.Boss == null) return true;
                if (!_bossSpawned)
                {
                    TrySpawnBoss(entityManager, step);
                    return false;
                }
                // Gone some other way than dying: nothing left to kill
                return _bossKilled || !entityManager.Exists(_boss);

            case DungeonStepKind.ReachExit:
                return IsPlayerAtExit(entityManager);

            case DungeonStepKind.Survive:
                _survived += Time.deltaTime;
                if (_survived >= step.SurviveSeconds) return true;
                UpdateWaves(entityManager, step);
                return false;

            default:
                return true;
        }
    }

    // ---------- Boss ----------

    void TrySpawnBoss(EntityManager entityManager, DungeonStep step)
    {
        var enemyManager = EnemyManager.Instance;
        if (enemyManager == null || !enemyManager.IsReady || !TryGetPlayer(entityManager, out var player)) return;

        _bossSpawned = true;
        _boss = enemyManager.Spawn(step.Boss, BossPosition(entityManager, player), riseIn: true);
        if (_boss == Entity.Null)
        {
            Debug.LogWarning($"DungeonManager: couldn't spawn the boss {step.Boss.name}, so its step is skipped.", this);
            return;
        }
        entityManager.AddComponent<DungeonBoss>(_boss);
        // The camera zooms out to keep it on screen while a player is near
        entityManager.AddComponentData(_boss, new CameraFocus { Radius = step.Boss.BodyRadius });
    }

    // The nearest built Boss Spawn Point, else BossDistance from the player
    Vector3 BossPosition(EntityManager entityManager, float3 player)
    {
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<DungeonBossSpawnPoint>(), ComponentType.ReadOnly<LocalToWorld>());
        using var points = query.ToComponentDataArray<LocalToWorld>(Allocator.Temp);
        float nearest = float.MaxValue;
        float3 position = default;
        foreach (var point in points)
        {
            float distance = math.distancesq(point.Position.xz, player.xz);
            if (distance >= nearest) continue;
            nearest = distance;
            position = point.Position;
        }
        return nearest < float.MaxValue ? position : AroundPlayer(entityManager, player, BossDistance);
    }

    // ---------- Survive ----------

    // A wave every WaveInterval (the first at once), unless MaxAlive enemies are alive (0: no limit): one of the step's groups, picked
    // by weight, rising out of the ground WaveDistance from the player in a random direction and coming for the player
    void UpdateWaves(EntityManager entityManager, DungeonStep step)
    {
        _nextWave -= Time.deltaTime;
        if (_nextWave > 0f) return;
        var enemyManager = EnemyManager.Instance;
        if (enemyManager == null || !enemyManager.IsReady || !TryGetPlayer(entityManager, out var player)) return;
        _nextWave = step.WaveInterval;

        using var alive = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Enemy>());
        if (step.MaxAlive > 0 && alive.CalculateEntityCount() >= step.MaxAlive) return;
        var wave = PickWave(step);
        if (wave == null) return;

        var point = AroundPlayer(entityManager, player, step.WaveDistance, UnityEngine.Random.value * math.PI * 2f);
        foreach (var entry in wave.Enemies)
            if (entry?.Definition != null && entry.Count > 0)
                enemyManager.SpawnGroup(entry.Definition, point, entry.Count, step.WaveSpread, riseIn: true, huntPlayer: true);
    }

    static PlacementEnemyGroup PickWave(DungeonStep step)
    {
        float total = 0f;
        foreach (var wave in step.Waves)
            if (wave != null && wave.Weight > 0f) total += wave.Weight;
        if (total <= 0f) return null;
        float roll = UnityEngine.Random.value * total;
        foreach (var wave in step.Waves)
        {
            if (wave == null || wave.Weight <= 0f) continue;
            roll -= wave.Weight;
            if (roll < 0f) return wave;
        }
        return step.Waves.FindLast(wave => wave != null && wave.Weight > 0f);
    }

    // ---------- Exit ----------

    // Any living player, this machine's or another's, at an exit
    bool IsPlayerAtExit(EntityManager entityManager)
    {
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<DungeonExit>(), ComponentType.ReadOnly<LocalToWorld>());
        using var exits = query.ToComponentDataArray<DungeonExit>(Allocator.Temp);
        using var transforms = query.ToComponentDataArray<LocalToWorld>(Allocator.Temp);
        using var players = LivingPlayers(entityManager);
        foreach (var player in players)
            for (int i = 0; i < exits.Length; i++)
                if (math.distance(player.xz, transforms[i].Position.xz) <= exits[i].Range) return true;
        return false;
    }

    // ---------- Finishing ----------

    void Complete(EntityManager entityManager)
    {
        _state = RunState.Complete;
        _portalPending = true;
        OpenReturnPortal(entityManager);
        Experience.GiveAllPlayers(entityManager, _dungeon.CompletionExperience);

        // Finished for every player's character
        var account = AccountManager.Instance;
        if (account != null && !string.IsNullOrEmpty(_dungeon.Id))
            for (int i = 0; i < GameInput.PlayerCount; i++)
                if (account.SelectedCharacterOf(i) != null) account.RecordDungeonCompleted(account.SelectedCharacterOf(i), _dungeon.Id);
        // And for the other machines' characters (NetDungeonReceiveSystem)
        if (!string.IsNullOrEmpty(_dungeon.Id))
        {
            FixedString64Bytes id = default;
            id.CopyFromTruncated(_dungeon.Id);
            NetPortal.Send(entityManager, new DungeonCompletedRpc { DungeonId = id });
        }
        Debug.Log($"DungeonManager: {_dungeon.DisplayName} complete.", this);
    }

    // Died in a Survive step, or out of time: every enemy disappears, the level's spawners that haven't triggered never
    // will, and only the return portal is left (opened once a player is alive). No completion loot, nothing saved.
    void Fail(EntityManager entityManager, string reason)
    {
        _state = RunState.Failed;
        _portalPending = true;

        using var enemies = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Enemy>());
        entityManager.AddComponent<DestroyWithChildren>(enemies);
        using var spawners = entityManager.CreateEntityQuery(ComponentType.ReadWrite<LevelEnemySpawner>());
        using var spawnerEntities = spawners.ToEntityArray(Allocator.Temp);
        foreach (var entity in spawnerEntities)
        {
            var spawner = entityManager.GetComponentData<LevelEnemySpawner>(entity);
            spawner.Triggered = true;
            entityManager.SetComponentData(entity, spawner);
        }
        Debug.Log($"DungeonManager: {_dungeon.DisplayName} failed: {reason}.", this);
    }

    // ReturnPortalDistance from the player (on the side away from the camera), leading to ReturnScene. After a finish
    // the completion loot lands between the player and the portal.
    void OpenReturnPortal(EntityManager entityManager)
    {
        if (!TryGetPlayer(entityManager, out var player)) return;
        _portalPending = false;

        float3 position = AroundPlayer(entityManager, player, ReturnPortalDistance);
        if (_state == RunState.Complete) DropLoot(entityManager, NetLootTable.Completion, math.lerp(player, position, 0.5f));
        if (_returnPortalPrefab == Entity.Null || !entityManager.Exists(_returnPortalPrefab)) return;
        float3 toPlayer = new float3(player.x - position.x, 0f, player.z - position.z);
        var turn = quaternion.LookRotationSafe(toPlayer, math.up());

        FixedString64Bytes scene = default, displayName = default;
        scene.CopyFromTruncated(_returnScene);
        displayName.CopyFromTruncated(_returnScene == GameBootstrap.TownScene ? "Town" : Regex.Replace(_returnScene, "(\\B[A-Z])", " $1"));
        NetPortal.PlaceReturnPortal(entityManager, _returnPortalPrefab, position, turn, scene, displayName);
        // The other machines place their own where this one is
        NetPortal.Send(entityManager, new ReturnPortalRpc { Position = position, Rotation = turn, Scene = scene, DisplayName = displayName });
    }

    // ---------- Helpers ----------

    // Where the boss, waves and return portal go around: player 1, or player 2 while player 1 is dead, or another
    // machine's player while this machine's are all dead
    bool TryGetPlayer(EntityManager entityManager, out float3 position)
    {
        position = default;
        for (int i = 0; i < GameInput.MaxPlayers; i++)
        {
            var characterManager = CharacterManager.For(i);
            var player = characterManager != null ? characterManager.Player : Entity.Null;
            if (player == Entity.Null || !entityManager.Exists(player) || !entityManager.HasComponent<LocalTransform>(player)) continue;
            position = entityManager.GetComponentData<LocalTransform>(player).Position;
            return true;
        }
        using var players = LivingPlayers(entityManager);
        if (players.Length == 0) return false;
        position = players[0];
        return true;
    }

    // Where every living player is, this machine's and the others' (on the host every player is a Player)
    static NativeList<float3> LivingPlayers(EntityManager entityManager)
    {
        var positions = new NativeList<float3>(Allocator.Temp);
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>(), ComponentType.ReadOnly<Health>(), ComponentType.ReadOnly<LocalTransform>());
        using var healths = query.ToComponentDataArray<Health>(Allocator.Temp);
        using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        for (int i = 0; i < healths.Length; i++)
            if (healths[i].CurrentValue > 0f) positions.Add(transforms[i].Position);
        return positions;
    }

    // A point on the ground distance meters from the player: first in the start direction (radians around Y from +X;
    // default away from the camera, up the screen), then around
    static float3 AroundPlayer(EntityManager entityManager, float3 player, float distance, float? direction = null)
    {
        float startAngle = 0f;
        var camera = Camera.main;
        if (direction is { } angleGiven) startAngle = angleGiven;
        else if (camera != null)
        {
            var forward = camera.transform.forward;
            if (new Vector2(forward.x, forward.z).sqrMagnitude > 0.0001f) startAngle = math.atan2(forward.z, forward.x);
        }

        bool hasPhysics = GroundPlacement.TryGetCollisionWorld(entityManager, out CollisionWorld collisionWorld);
        float3 first = default;
        for (int i = 0; i < GroundTries; i++)
        {
            // 0, +45, -45, +90, -90, ... degrees from the start direction
            float step = (i + 1) / 2 * (math.PI * 2f / GroundTries) * (i % 2 == 0 ? 1f : -1f);
            float angle = startAngle + step;
            var point = player + new float3(math.cos(angle), 0f, math.sin(angle)) * distance;
            if (i == 0) first = point;
            if (!hasPhysics) return point;
            if (GroundPlacement.TryFindGround(collisionWorld, point, out var ground) && math.abs(ground.y - player.y) < 2f) return ground;
        }
        return first;
    }
}
