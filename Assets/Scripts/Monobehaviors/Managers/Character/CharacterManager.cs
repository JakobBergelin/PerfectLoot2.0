using Characters;
using EnemySkills;
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// Spawns the player from its visual-only prefab (baked by PlayerPrefabAuthoring in a subscene), adds the
/// gameplay components, applies class/resource/level, and respawns the player after it dies. One per local player
/// (PlayerIndex; player 2's is made by CoopManager), like InventoryManager, ActiveSkillManager and PassiveSkillManager.
/// </summary>
public class CharacterManager : MonoBehaviour, IPerPlayer
{
    static readonly CharacterManager[] _players = new CharacterManager[GameInput.MaxPlayers];

    /// <summary>Player 1's.</summary>
    public static CharacterManager Instance => _players[0];

    /// <summary>A local player's (0 is player 1), or null when that player isn't in the game.</summary>
    public static CharacterManager For(int playerIndex) =>
        playerIndex >= 0 && playerIndex < _players.Length ? _players[playerIndex] : null;

    /// <summary>Which local player's this is: 0 is player 1 (the rig's own); player 2's copy is made by CoopManager.</summary>
    public int PlayerIndex { get; set; }

    /// <summary>Raised after every (re)spawn. Other managers re-push their state to the new player entity.</summary>
    public static event Action<Entity> PlayerSpawned;

    /// <summary>Raised when the player has died (its entity is gone); it respawns after RespawnDelay.</summary>
    public static event Action PlayerDied;

    [Header("Spawning")]
    [Tooltip("Base stats before class, gear, passives and buffs")]
    public PlayerBaseStats BaseStats;
    [Tooltip("Seconds between the player dying and respawning")]
    public float RespawnDelay = 2f;

    [Header("Selected (set from the character picked in Character Select; empty = random from the lists below at start)")]
    public CharacterClass Class;
    public ResourceDefinition Resource;

    [Tooltip("The player's level: pushed at spawn and whenever this value is changed here, and follows the levels it earns. " +
             "Class attributes scale with level.")]
    [Range(1, Experience.MaxLevel)] public int StartingLevel = 1;

    [Header("Everything that can be rolled at start")]
    public List<CharacterClass> AllClasses = new List<CharacterClass>();
    public List<ResourceDefinition> AllResources = new List<ResourceDefinition>();

    [Header("Testing")]
    [Tooltip("Applied by the \"Apply test effect to player\" context menu item")]
    public Effects.EffectDefinition TestEffect;
    public int TestStacks = 1;
    [Tooltip("Seconds for test buffs (Broken Promises, Invulnerable); status effects use StatusEffectSettings")]
    public float TestDuration = 3f;
    [Tooltip("How far from the player 'Spawn test second player' puts it, in meters")]
    public float TestPlayerOffset = 15f;
    [Tooltip("What 'Spawn test companions' spawns, TestCompanionCount of each")]
    public List<BoidSettings> TestCompanions = new List<BoidSettings>
    {
        new BoidSettings { Position = BoidPosition.Behind, Distance = 3f, Spread = 2f, Leash = 10f, AttackDistance = 2f, CatchUpMultiplier = 1.5f, TeleportDistance = 20f },
        new BoidSettings { Position = BoidPosition.Left, Distance = 3f, Spread = 2f, Leash = 10f, AttackDistance = 2f, CatchUpMultiplier = 1.5f, TeleportDistance = 20f },
        new BoidSettings { Position = BoidPosition.Right, Distance = 3f, Spread = 2f, Leash = 10f, AttackDistance = 2f, CatchUpMultiplier = 1.5f, TeleportDistance = 20f },
    };
    public int TestCompanionCount = 3;
    [Tooltip("What test companions use. Empty: they only follow and chase.")]
    public List<EnemySkill> TestCompanionSkills = new List<EnemySkill>();
    [Tooltip("How far test companions see enemies, in meters")]
    public float TestCompanionSight = 15f;

    /// <summary>The current player entity, or Entity.Null while dead / not spawned yet.</summary>
    public Entity Player => _player;

    [ContextMenu("Spawn test second player")]
    void SpawnTestPlayer()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (!Application.isPlaying || world == null || !world.EntityManager.Exists(_player)) return;
        var entityManager = world.EntityManager;

        using var prefabQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerPrefab>());
        var testPlayer = entityManager.Instantiate(prefabQuery.GetSingleton<PlayerPrefab>().Value);
        var testTransform = entityManager.GetComponentData<LocalTransform>(testPlayer);
        testTransform.Position = entityManager.GetComponentData<LocalTransform>(_player).Position + new Unity.Mathematics.float3(TestPlayerOffset, 0f, 0f);
        entityManager.SetComponentData(testPlayer, testTransform);
        PlayerEntitySetup.AddTestPlayerComponents(entityManager, testPlayer, BaseStats != null ? BaseStats.Stats : DefaultBaseStats.Stats, fieldSlot: 1);
    }

    [ContextMenu("Spawn test companions")]
    void SpawnTestCompanions()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (!Application.isPlaying || world == null || !world.EntityManager.Exists(_player)) return;
        var entityManager = world.EntityManager;

        using var prefabQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerPrefab>());
        var position = entityManager.GetComponentData<LocalTransform>(_player).Position;
        foreach (var settings in TestCompanions)
        for (int i = 0; i < TestCompanionCount; i++)
        {
            var companion = entityManager.Instantiate(prefabQuery.GetSingleton<PlayerPrefab>().Value);
            var transform = entityManager.GetComponentData<LocalTransform>(companion);
            transform.Position = position;
            entityManager.SetComponentData(companion, transform);
            CompanionEntitySetup.AddGameplayComponents(entityManager, companion, _player, TestCompanionSight, TestCompanionSkills);
            CompanionEntitySetup.AddMovementComponents(entityManager, companion, BaseStats != null ? BaseStats.Stats : DefaultBaseStats.Stats, settings);
        }
    }

    [ContextMenu("Apply test effect to player")]
    void ApplyTestEffect() => RequestTestEffect(remove: false);

    [ContextMenu("Remove test effect from player")]
    void RemoveTestEffect() => RequestTestEffect(remove: true);

    // Applies or removes the test effect directly on the player's effect components. Status effects use
    // StatusEffectSettings; buffs use TestDuration / TestStacks.
    void RequestTestEffect(bool remove)
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (!Application.isPlaying || TestEffect == null || world == null || !world.EntityManager.Exists(_player))
        {
            Debug.LogWarning("Needs Play mode, a spawned player and a TestEffect.", this);
            return;
        }
        var entityManager = world.EntityManager;
        using var settingsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<StatusEffectConfig>());
        var settings = settingsQuery.IsEmpty ? default : settingsQuery.GetSingleton<StatusEffectConfig>();
        int stacks = Mathf.Max(1, TestStacks);

        switch (TestEffect.Type)
        {
            case EffectType.BrokenPromises:
                if (remove) entityManager.SetComponentEnabled<BrokenPromisesBuff>(_player, false);
                else SetEffect(entityManager, new BrokenPromisesBuff { TimeRemaining = TestDuration, Duration = TestDuration, CriticalChanceBonus = 1f });
                break;
            case EffectType.DeathsDoor:
                if (remove) entityManager.SetComponentEnabled<DeathsDoorBuff>(_player, false);
                else SetEffect(entityManager, new DeathsDoorBuff { CriticalChanceBonus = 1f });
                break;
            case EffectType.CounterStrike:
                if (remove) entityManager.SetComponentEnabled<CounterStrikeCharge>(_player, false);
                else SetEffect(entityManager, new CounterStrikeCharge { Stacks = stacks });
                break;
            case EffectType.Refraction:
                if (remove) entityManager.SetComponentEnabled<Refraction>(_player, false);
                else SetEffect(entityManager, new Refraction { Stacks = stacks });
                break;
            case EffectType.Invulnerable:
                if (remove) entityManager.SetComponentEnabled<Invulnerable>(_player, false);
                else SetEffect(entityManager, new Invulnerable { TimeRemaining = TestDuration, Duration = TestDuration });
                break;
            case EffectType.Shadowstep:
                if (remove) entityManager.SetComponentEnabled<ShadowstepBuff>(_player, false);
                else SetEffect(entityManager, new ShadowstepBuff { TimeRemaining = TestDuration, Duration = TestDuration, AttackSpeedBonus = 0.15f, Range = 30f });
                break;
            case EffectType.Bloodrush:
                if (remove) entityManager.SetComponentEnabled<BloodrushBuff>(_player, false);
                else SetEffect(entityManager, new BloodrushBuff { Stacks = stacks, AttackSpeedPerStack = 0.03f, TimeRemaining = TestDuration, Duration = TestDuration });
                break;
            case EffectType.Prism:
                if (remove) entityManager.SetComponentEnabled<PrismBuff>(_player, false);
                else SetEffect(entityManager, new PrismBuff { DamageBonus = 0.2f, TimeRemaining = TestDuration, Duration = TestDuration });
                break;
            case EffectType.Anchor:
                if (remove) entityManager.SetComponentEnabled<AnchorBuff>(_player, false);
                else SetEffect(entityManager, new AnchorBuff { Stacks = stacks, ArmorPerStack = 2 });
                break;
            case EffectType.Momentum:
                if (remove) entityManager.SetComponentEnabled<MomentumBuff>(_player, false);
                else SetEffect(entityManager, new MomentumBuff { Stacks = stacks, DamagePerStack = 0.03f });
                break;
            case EffectType.Ascetic:
                if (remove) entityManager.SetComponentEnabled<AsceticBuff>(_player, false);
                else SetEffect(entityManager, new AsceticBuff { DamageBonus = 0.25f });
                break;
            case EffectType.Afterglow:
                if (remove) entityManager.SetComponentEnabled<AfterglowBuff>(_player, false);
                else SetEffect(entityManager, new AfterglowBuff { DamageBonus = 0.15f, TimeRemaining = TestDuration, Duration = TestDuration });
                break;
            case EffectType.Bleeding:
                if (remove) { entityManager.GetBuffer<BleedInstance>(_player).Clear(); entityManager.SetComponentEnabled<Bleeding>(_player, false); }
                else
                {
                    for (int i = 0; i < stacks; i++)
                        entityManager.GetBuffer<BleedInstance>(_player).Add(new BleedInstance { TimeRemaining = settings.BleedDuration, Duration = settings.BleedDuration });
                    entityManager.SetComponentEnabled<Bleeding>(_player, true);
                }
                break;
            case EffectType.Burning:
                if (remove) { entityManager.GetBuffer<BurnInstance>(_player).Clear(); entityManager.SetComponentEnabled<Burning>(_player, false); }
                else
                {
                    for (int i = 0; i < stacks; i++)
                        entityManager.GetBuffer<BurnInstance>(_player).Add(new BurnInstance { DamagePerSecond = 1f, TimeRemaining = settings.BurnDuration, Duration = settings.BurnDuration });
                    entityManager.SetComponentEnabled<Burning>(_player, true);
                }
                break;
            case EffectType.Poisoned:
                if (remove) entityManager.SetComponentEnabled<Poisoned>(_player, false);
                else SetEffect(entityManager, new Poisoned { TimeRemaining = settings.PoisonDuration, Duration = settings.PoisonDuration, DamagePerSecond = settings.PoisonDamagePerSecond });
                break;
            case EffectType.Chilled:
                if (remove) entityManager.SetComponentEnabled<Chilled>(_player, false);
                else SetEffect(entityManager, new Chilled { Stacks = Mathf.Min(stacks, settings.ChillMaxStacks), TimeRemaining = settings.ChillDuration, Duration = settings.ChillDuration });
                break;
            case EffectType.Frozen:
                if (remove) entityManager.SetComponentEnabled<Frozen>(_player, false);
                else SetEffect(entityManager, new Frozen { TimeRemaining = settings.FrozenDuration, Duration = settings.FrozenDuration });
                break;
        }
    }

    void SetEffect<T>(EntityManager entityManager, T effect) where T : unmanaged, IComponentData, IEnableableComponent
    {
        entityManager.SetComponentData(_player, effect);
        entityManager.SetComponentEnabled<T>(_player, true);
    }

    Entity _player;
    World _playerWorld;     // GameSceneLoader replaces the world on scene changes; _player belongs to this one
    bool _hasSpawned;
    float _respawnTimer;

    // What is currently applied to the player entity, so it can be replaced
    ResourceDefinition _appliedResource;
    CharacterClass _appliedClass;   // Whose class passive the player has
    int? _pushedLevel;
    bool _dirty;

    // The played character (null without the main menu) and the experience earned toward the next level, kept across
    // respawns and scenes and saved on it
    AccountCharacter _character;
    int _experience;

    /// <summary>The experience earned toward the next level (sent with the loadout in someone else's game).</summary>
    public int EarnedExperience => _experience;

    void Awake()
    {
        if (For(PlayerIndex) != null && For(PlayerIndex) != this) { Destroy(gameObject); return; }
        _players[PlayerIndex] = this;
    }

    void OnEnable() => GameSceneLoader.Loading += SaveProgress;
    void OnDisable() => GameSceneLoader.Loading -= SaveProgress;
    void OnApplicationQuit() => SaveProgress();

    void Start()
    {
        // Picked in Character Select; a game scene played without the main menu has no selected character
        var selected = AccountManager.Instance != null ? AccountManager.Instance.SelectedCharacterOf(PlayerIndex) : null;
        if (selected != null) UseCharacter(selected);
        else RandomizeEmpty();
    }

    void UseCharacter(AccountCharacter character)
    {
        Class = AllClasses.Find(c => c != null && c.Type == character.Class);
        Resource = AllResources.Find(r => r != null && r.Type == character.Resource);
        _character = character;
        StartingLevel = Mathf.Clamp(character.Level, 1, Experience.MaxLevel);
        _experience = Mathf.Max(0, character.Experience);
        if (Class == null && character.Class != ClassType.None)
            Debug.LogWarning($"CharacterManager: AllClasses has no {character.Class} for {character.Name}.", this);
        if (Resource == null && character.Resource != ResourceType.None)
            Debug.LogWarning($"CharacterManager: AllResources has no {character.Resource} for {character.Name}.", this);
    }

    void OnDestroy()
    {
        if (For(PlayerIndex) != this) return;
        _players[PlayerIndex] = null;
        // A player leaving saves their progress and takes their entity along (player 1's goes with the scene)
        if (PlayerIndex > 0) SaveProgress();
        var world = World.DefaultGameObjectInjectionWorld;
        if (PlayerIndex > 0 && NetSession.IsHost && world != null && world.IsCreated && world == _playerWorld && world.EntityManager.Exists(_player))
            world.EntityManager.DestroyEntity(_player);
    }

    void Update()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;
        var entityManager = world.EntityManager;

        // A new scene's world: spawn there at once (the old entity could even match one in the new world)
        if (world != _playerWorld)
        {
            _playerWorld = world;
            _player = Entity.Null;
            _hasSpawned = false;
            _groundWait = 0f;
        }

        // Died: CheckDeathSystem destroyed the entity. A ghost lingers for Netcode's cleanup, and a command buffer can
        // even add to it meanwhile, which keeps it around, so a player without its transform is gone too: finish it off
        if (_player != Entity.Null && (!entityManager.Exists(_player) || !entityManager.HasComponent<LocalTransform>(_player)))
        {
            if (entityManager.Exists(_player) && NetSession.IsHost) entityManager.DestroyEntity(_player);
            _player = Entity.Null;
            _respawnTimer = RespawnDelay;
            PlayerDied?.Invoke();
        }

        if (_player == Entity.Null)
        {
            // In someone else's game the host makes this player's ghost: take it over when it arrives
            if (!NetSession.IsHost)
            {
                TryAdopt(entityManager);
                return;
            }
            if (_hasSpawned)
            {
                _respawnTimer -= Time.deltaTime;
                if (_respawnTimer > 0f) return;
            }
            // Retries every frame until the subscene with PlayerPrefabAuthoring has loaded
            TrySpawn(entityManager);
            return;
        }

        if (_dirty) TryPushCharacter();
        TrackProgress(entityManager);
    }

    // Keeps what the player earned (ExperienceSystem), for respawns and on the character. Saved right away on a level-up.
    void TrackProgress(EntityManager entityManager)
    {
        // A dying player can still exist for a frame without its components
        if (!entityManager.HasComponent<Level>(_player)) return;
        var level = entityManager.GetComponentData<Level>(_player);
        // In someone else's game the level arrives from the host, which has none until it has this loadout (NetLoadout)
        if (level.Value < 1) return;
        if (level.Value == StartingLevel && level.Experience == _experience) return;
        bool levelledUp = level.Value > StartingLevel;
        StartingLevel = level.Value;
        _pushedLevel = level.Value;
        _experience = level.Experience;
        if (_character != null && AccountManager.Instance != null)
            AccountManager.Instance.SetProgress(_character, StartingLevel, _experience, save: levelledUp);
    }

    // Leaving the scene or quitting: the latest progress, then the save
    void SaveProgress()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated && world == _playerWorld && _player != Entity.Null && world.EntityManager.Exists(_player))
            TrackProgress(world.EntityManager);
        if (_character != null && AccountManager.Instance != null)
            AccountManager.Instance.SetProgress(_character, StartingLevel, _experience, save: true);
    }

    void TrySpawn(EntityManager entityManager)
    {
        using var prefabQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerPrefab>());
        if (prefabQuery.IsEmpty) return;
        // A generated level must exist first, or the player would fall through it
        if (!LevelGeneration.LevelState.IsReady(entityManager)) return;
        var prefab = prefabQuery.GetSingleton<PlayerPrefab>().Value;
        if (prefab == Entity.Null)
        {
            Debug.LogError("PlayerPrefabAuthoring has no PlayerVisualPrefab assigned.");
            enabled = false;
            return;
        }

        if (!TryFindSpawnPosition(entityManager, prefab, out var spawnPosition)) return;

        // The subscene is loaded at this point, so the settings singletons should be too. Without them movement,
        // damage and stat systems never run.
        using var settingsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MovementConfig>(), ComponentType.ReadOnly<CombatConfig>(), ComponentType.ReadOnly<StatusEffectConfig>());
        if (settingsQuery.IsEmpty)
            Debug.LogError("No game settings found. Add the GameSettings prefab (GameSettingsAuthoring) to the Entities subscene.", this);

        if (BaseStats == null)
            Debug.LogWarning("CharacterManager has no PlayerBaseStats, using defaults.", this);
        var baseStats = BaseStats != null ? BaseStats.Stats : DefaultBaseStats.Stats;

        _player = entityManager.Instantiate(prefab);
        var spawnTransform = entityManager.GetComponentData<LocalTransform>(_player);
        spawnTransform.Position = spawnPosition;
        entityManager.SetComponentData(_player, spawnTransform);

        PlayerEntitySetup.AddGameplayComponents(entityManager, _player, baseStats, PlayerIndex);

        // Fresh entity: nothing applied yet
        _appliedResource = null;
        _appliedClass = null;
        _pushedLevel = null;
        _hasSpawned = true;
        // Owned by this machine's own connection, as this local player (NetInputSendSystem sends its input like a client's)
        NetPlayerSetup.MarkLocal(entityManager, _player, PlayerIndex);

        PushCharacter();

        PlayerSpawned?.Invoke(_player);
    }

    // On a client: this machine's player ghost with this PlayerIndex, made by the host (NetPlayerSpawnSystem). It gets the
    // local player's components, so the camera follows it and its input is read and sent. Its gameplay stays on the host.
    void TryAdopt(EntityManager entityManager)
    {
        var ghost = NetPlayerSetup.FindOwned(entityManager, PlayerIndex);
        if (ghost == Entity.Null) return;
        _player = ghost;
        _hasSpawned = true;
        NetPlayerSetup.AddLocalPlayerComponents(entityManager, ghost, PlayerIndex);
        PlayerSpawned?.Invoke(_player);
    }

    // Where player 2 appears from player 1 (and player 1 from player 2, the other way)
    static readonly Vector3 JoinOffset = new Vector3(2f, 0f, 0f);

    // Seconds to keep looking for ground before spawning at the plain position anyway
    const float MaxGroundWait = 2f;
    float _groundWait;
    Vector3 _groundWaitPosition;

    /// <summary>
    /// The level's player start (or this object's position without a level), or in co-op next to the other player,
    /// standing on the ground below it. A respawn in a dungeon's Reach Exit step is always at the start
    /// (DungeonManager.RespawnAtStart). False while the ground isn't in the physics world yet (e.g. the frame the level
    /// appears); after MaxGroundWait at the same position it gives up and uses the plain position, with the player's feet
    /// at its height.
    /// </summary>
    bool TryFindSpawnPosition(EntityManager entityManager, Entity prefab, out Vector3 position)
    {
        position = transform.position;
        if (LevelGeneration.LevelState.TryGetPlayerStart(entityManager, out var playerStart)) position = playerStart;
        // In co-op: next to the other player while they're in the level (player 2 joining, anyone respawning)
        bool backToStart = _hasSpawned && DungeonManager.RespawnAtStart;
        for (int i = 0; i < _players.Length && !backToStart; i++)
        {
            var other = _players[i];
            if (i == PlayerIndex || other == null || other._player == Entity.Null || !entityManager.Exists(other._player)) continue;
            position = (Vector3)entityManager.GetComponentData<LocalTransform>(other._player).Position + (PlayerIndex > i ? JoinOffset : -JoinOffset);
            break;
        }
        // Time spent waiting for ground somewhere else doesn't count
        if (position != _groundWaitPosition)
        {
            _groundWaitPosition = position;
            _groundWait = 0f;
        }

        float bottomOffset = LevelGeneration.GroundPlacement.BottomOffset(entityManager, prefab);
        if (LevelGeneration.GroundPlacement.TryGetCollisionWorld(entityManager, out var collisionWorld) &&
            LevelGeneration.GroundPlacement.TryPlace(collisionWorld, position, bottomOffset, out var placed))
        {
            position = placed;
            _groundWait = 0f;
            return true;
        }

        _groundWait += Time.deltaTime;
        if (_groundWait < MaxGroundWait) return false;
        Debug.LogWarning($"CharacterManager: no ground under the spawn position {position}; spawning there anyway.", this);
        _groundWait = 0f;
        // Feet at the start's height rather than the capsule's middle, so the floor (if there is one) can still be followed
        position.y += bottomOffset + LevelGeneration.GroundPlacement.Skin;
        return true;
    }

    PlayerBaseStats _defaultBaseStats;
    PlayerBaseStats DefaultBaseStats => _defaultBaseStats != null ? _defaultBaseStats : (_defaultBaseStats = ScriptableObject.CreateInstance<PlayerBaseStats>());

    void RandomizeEmpty()
    {
        if (Class == null) Class = PickRandom(AllClasses);
        if (Resource == null) Resource = PickRandom(AllResources);
    }

    static T PickRandom<T>(List<T> options) where T : Object
    {
        var valid = options.FindAll(o => o != null);
        if (valid.Count == 0)
        {
            Debug.LogWarning($"CharacterManager has no {typeof(T).Name} to pick from.");
            return null;
        }
        return valid[Random.Range(0, valid.Count)];
    }

    /// <summary>Applies Class, Resource and StartingLevel to the player.</summary>
    [ContextMenu("Push character")]
    public void PushCharacter()
    {
        _dirty = true;
        TryPushCharacter();
    }

    void TryPushCharacter()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        var entityManager = world.EntityManager;
        if (_player == Entity.Null || !entityManager.Exists(_player)) return;
        // In someone else's game the host runs this player: the loadout is sent to it instead (NetLoadoutSendSystem)
        if (!NetSession.IsHost)
        {
            NetLoadoutSendSystem.MarkDirty(PlayerIndex);
            _dirty = false;
            return;
        }

        ApplyCharacter(entityManager, _player, Class, Resource, ref _appliedClass, ref _appliedResource);
        // Only overwrite Level when StartingLevel changed (or on spawn), so earned experience isn't reset
        if (_pushedLevel != StartingLevel) SetLevel(entityManager, _player, StartingLevel, _experience);
        _pushedLevel = StartingLevel;
        _dirty = false;
    }

    /// <summary>
    /// Gives a player entity this class and resource, removing the ones applied to it before (appliedClass and
    /// appliedResource, updated). Used for this machine's players and, on the host, for remote ones (NetLoadoutReceiveSystem).
    /// </summary>
    public static void ApplyCharacter(EntityManager entityManager, Entity player, CharacterClass characterClass, ResourceDefinition resource,
                                      ref CharacterClass appliedClass, ref ResourceDefinition appliedResource)
    {
        if (appliedClass != null && appliedClass != characterClass) appliedClass.RemovePassive(entityManager, player);
        if (characterClass != null) characterClass.Apply(entityManager, player);
        else CharacterClass.Remove(entityManager, player);

        if (appliedResource != null && appliedResource != resource) appliedResource.Remove(entityManager, player);
        if (resource != null) resource.Apply(entityManager, player);

        // Apply/Remove are structural changes, so set the records afterwards
        entityManager.SetComponentData(player, new UnitClass { Type = characterClass != null ? characterClass.Type : ClassType.None });
        entityManager.SetComponentData(player, new UnitResource { Type = resource != null ? resource.Type : ResourceType.None });
        appliedClass = characterClass;
        appliedResource = resource;
    }

    public static void SetLevel(EntityManager entityManager, Entity player, int value, int experience) =>
        entityManager.SetComponentData(player, new Level { Value = value, Experience = experience });

    void OnValidate()
    {
        // Lets class/resource/level be changed in the Inspector during play mode
        if (Application.isPlaying && Instance == this) PushCharacter();

#if UNITY_EDITOR
        WarnAboutList(AllClasses, c => c.Type.ToString(), Enum.GetNames(typeof(ClassType)), "AllClasses");
        WarnAboutList(AllResources, r => r.Type.ToString(), Enum.GetNames(typeof(ResourceType)), "AllResources");
#endif
    }

#if UNITY_EDITOR
    void WarnAboutList<T>(List<T> list, Func<T, string> typeOf, string[] allTypes, string listName) where T : Object
    {
        var seen = new HashSet<string>();
        foreach (var entry in list)
        {
            if (entry == null) { Debug.LogWarning($"{name}: {listName} has an empty entry.", this); continue; }
            if (!seen.Add(typeOf(entry))) Debug.LogWarning($"{name}: {typeOf(entry)} is in {listName} more than once.", this);
        }
        foreach (var type in allTypes)
            if (type != "None" && !seen.Contains(type))
                Debug.LogWarning($"{name}: {listName} is missing {type}. Run Tools > Create missing character assets.", this);
    }
#endif
}
