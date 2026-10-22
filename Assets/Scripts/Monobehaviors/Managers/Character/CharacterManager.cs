using Characters;
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// Spawns the player from its visual-only prefab (baked by PlayerPrefabAuthoring in a subscene), adds the
/// gameplay components, applies class/resource/level, and respawns the player after it dies.
/// </summary>
public class CharacterManager : MonoBehaviour
{
    public static CharacterManager Instance;

    /// <summary>Raised after every (re)spawn. Other managers re-push their state to the new player entity.</summary>
    public static event Action<Entity> PlayerSpawned;

    [Header("Spawning")]
    [Tooltip("Base stats before class, gear, passives and buffs")]
    public PlayerBaseStats BaseStats;
    [Tooltip("Where the player spawns. Uses this object's position when empty.")]
    public Transform SpawnPoint;
    [Tooltip("Seconds between the player dying and respawning")]
    public float RespawnDelay = 3f;

    [Header("Selected (empty = random from the lists below at start)")]
    public CharacterClass Class;
    public ResourceDefinition Resource;

    [Tooltip("Pushed to the player's Level at spawn and whenever this value changes. Class attributes scale with level.")]
    public int StartingLevel = 1;

    [Header("Everything that can be rolled at start")]
    public List<CharacterClass> AllClasses = new List<CharacterClass>();
    public List<ResourceDefinition> AllResources = new List<ResourceDefinition>();

    [Header("Testing")]
    [Tooltip("Applied by the \"Apply test effect to player\" context menu item")]
    public Effects.EffectDefinition TestEffect;
    public int TestStacks = 1;
    [Tooltip("Seconds for test buffs (Broken Promises, Invulnerable); status effects use StatusEffectSettings")]
    public float TestDuration = 3f;

    /// <summary>The current player entity, or Entity.Null while dead / not spawned yet.</summary>
    public Entity Player => _player;

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
            case EffectType.Refraction:
                if (remove) entityManager.SetComponentEnabled<Refraction>(_player, false);
                else SetEffect(entityManager, new Refraction { Stacks = stacks });
                break;
            case EffectType.Invulnerable:
                if (remove) entityManager.SetComponentEnabled<Invulnerable>(_player, false);
                else SetEffect(entityManager, new Invulnerable { TimeRemaining = TestDuration, Duration = TestDuration });
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
    bool _hasSpawned;
    float _respawnTimer;

    // What is currently applied to the player entity, so it can be replaced
    ResourceDefinition _appliedResource;
    int? _pushedLevel;
    bool _dirty;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start() => RandomizeEmpty();

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;
        var entityManager = world.EntityManager;

        // Died: CheckDeathSystem destroyed the entity
        if (_player != Entity.Null && !entityManager.Exists(_player))
        {
            _player = Entity.Null;
            _respawnTimer = RespawnDelay;
        }

        if (_player == Entity.Null)
        {
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
    }

    void TrySpawn(EntityManager entityManager)
    {
        using var prefabQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerPrefab>());
        if (prefabQuery.IsEmpty) return;
        var prefab = prefabQuery.GetSingleton<PlayerPrefab>().Value;
        if (prefab == Entity.Null)
        {
            Debug.LogError("PlayerPrefabAuthoring has no PlayerVisualPrefab assigned.");
            enabled = false;
            return;
        }

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
        spawnTransform.Position = SpawnPoint != null ? SpawnPoint.position : transform.position;
        entityManager.SetComponentData(_player, spawnTransform);

        PlayerEntitySetup.AddGameplayComponents(entityManager, _player, baseStats);

        // Fresh entity: nothing applied yet
        _appliedResource = null;
        _pushedLevel = null;
        _hasSpawned = true;
        PushCharacter();

        PlayerSpawned?.Invoke(_player);
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

        if (Class != null) Class.Apply(entityManager, _player);
        else CharacterClass.Remove(entityManager, _player);

        if (_appliedResource != null && _appliedResource != Resource) _appliedResource.Remove(entityManager, _player);
        if (Resource != null) Resource.Apply(entityManager, _player);

        // Apply/Remove are structural changes, so set the records afterwards
        entityManager.SetComponentData(_player, new UnitClass { Type = Class != null ? Class.Type : ClassType.None });
        entityManager.SetComponentData(_player, new UnitResource { Type = Resource != null ? Resource.Type : ResourceType.None });

        // Only overwrite Level when StartingLevel changed (or on spawn), so a future XP system isn't reset
        if (_pushedLevel != StartingLevel)
        {
            var level = entityManager.GetComponentData<Level>(_player);
            level.Value = StartingLevel;
            entityManager.SetComponentData(_player, level);
        }

        _appliedResource = Resource;
        _pushedLevel = StartingLevel;
        _dirty = false;
    }

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
