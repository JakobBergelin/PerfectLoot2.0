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

    /// <summary>The current player entity, or Entity.Null while dead / not spawned yet.</summary>
    public Entity Player => _player;

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
