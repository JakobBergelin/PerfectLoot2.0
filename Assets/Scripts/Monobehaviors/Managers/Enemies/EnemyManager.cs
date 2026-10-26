using Enemies;
using LevelGeneration;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Spawns enemies from EnemyDefinitions: instantiates the baked visual prefab and adds the gameplay components.
/// Only handles how to spawn; whatever decides when and where (waves, rooms, ...) calls Spawn / SpawnGroup.
/// </summary>
public class EnemyManager : MonoBehaviour
{
    public static EnemyManager Instance;

    [Header("Testing")]
    [Tooltip("Spawned by the \"Spawn test group\" context menu item")]
    public EnemyDefinition TestDefinition;
    public int TestCount = 5;
    [Tooltip("Spawned by the \"Spawn dummy\" context menu item")]
    public EnemyDefinition DummyDefinition;

    [Header("Spawning")]
    [Tooltip("Seconds enemies spawned with riseIn take to rise out of the ground")]
    public float RiseDuration = 0.6f;

    // Random spots a group member tries before settling for one without ground under it
    const int GroundTries = 6;

    World _queryWorld;
    EntityQuery _prefabsQuery;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>True once the Entities subscene with EnemyPrefabsAuthoring is loaded.</summary>
    public bool IsReady => TryGetPrefabs(out _, out _);

    /// <summary>Spawns one enemy at the position. Returns Entity.Null if not ready or the definition isn't registered.</summary>
    public Entity Spawn(EnemyDefinition definition, Vector3 position, bool riseIn = false)
    {
        using var positions = new NativeArray<Vector3>(1, Allocator.Temp);
        var spawned = positions;
        spawned[0] = position;
        return SpawnAt(definition, positions, riseIn);
    }

    /// <summary>
    /// Spawns count enemies at random points within radius of center (on the same height). riseIn: they rise out of
    /// the ground over RiseDuration instead of appearing at once.
    /// </summary>
    public void SpawnGroup(EnemyDefinition definition, Vector3 center, int count, float radius, bool riseIn = false)
    {
        if (count <= 0) return;
        using var positions = new NativeArray<Vector3>(count, Allocator.Temp);
        var spawned = positions;
        // A spot over a wall or water has no ground under it: try a few others (the last try is used regardless)
        CollisionWorld collisionWorld = default;
        var world = World.DefaultGameObjectInjectionWorld;
        bool hasPhysics = world != null && world.IsCreated && GroundPlacement.TryGetCollisionWorld(world.EntityManager, out collisionWorld);
        for (int i = 0; i < count; i++)
        {
            for (int attempt = 0; attempt < GroundTries; attempt++)
            {
                var offset = Random.insideUnitCircle * radius;
                spawned[i] = center + new Vector3(offset.x, 0f, offset.y);
                if (!hasPhysics || GroundPlacement.TryFindGround(collisionWorld, spawned[i], out _)) break;
            }
        }
        SpawnAt(definition, positions, riseIn);
    }

    /// <summary>
    /// Builds one fully set-up template from the definition, instantiates it once per position in a single batch and
    /// removes the template. Adding the gameplay components once instead of per enemy keeps big groups cheap.
    /// Returns the first spawned enemy, or Entity.Null.
    /// </summary>
    Entity SpawnAt(EnemyDefinition definition, NativeArray<Vector3> positions, bool riseIn)
    {
        if (definition == null) { Debug.LogWarning("EnemyManager.Spawn: no definition.", this); return Entity.Null; }
        if (!TryGetPrefabs(out var entityManager, out var prefabs))
        {
            Debug.LogWarning("EnemyManager.Spawn: enemy prefabs aren't loaded yet. Is EnemyPrefabs in the Entities subscene?", this);
            return Entity.Null;
        }
        if (!TryFindPrefab(prefabs, definition, out var prefab))
        {
            Debug.LogWarning($"EnemyManager.Spawn: {definition.name} isn't in EnemyPrefabsAuthoring's Definitions.", this);
            return Entity.Null;
        }

        // Template: the baked visual prefab plus the definition's gameplay components, turned back into a prefab
        // (on the whole linked group, so its health bar isn't simulated or drawn either)
        var template = entityManager.Instantiate(prefab);
        EnemyEntitySetup.AddGameplayComponents(entityManager, template, definition);

        // Rising out of the ground: start a body height under the spawn point; it doesn't move or act until risen (SpawnRiseSystem)
        float riseDepth = 2f * Mathf.Max(0.01f, definition.Scale);
        if (riseIn) entityManager.AddComponentData(template, new SpawnRise { Depth = riseDepth, Duration = RiseDuration });
        if (entityManager.HasBuffer<LinkedEntityGroup>(template))
        {
            using var group = entityManager.GetBuffer<LinkedEntityGroup>(template).Reinterpret<Entity>().ToNativeArray(Allocator.Temp);
            entityManager.AddComponent<Prefab>(group);
        }
        else
        {
            entityManager.AddComponent<Prefab>(template);
        }

        // On the ground: the collider's lowest point just above the floor under each position (unchanged where there's none)
        float bottomOffset = GroundPlacement.BottomOffset(entityManager, template);
        bool hasPhysics = GroundPlacement.TryGetCollisionWorld(entityManager, out var collisionWorld);

        using var enemies = entityManager.Instantiate(template, positions.Length, Allocator.Temp);
        var templateTransform = entityManager.GetComponentData<LocalTransform>(template);
        for (int i = 0; i < enemies.Length; i++)
        {
            var transform = templateTransform;
            transform.Position = positions[i];
            if (hasPhysics && GroundPlacement.TryPlace(collisionWorld, transform.Position, bottomOffset, out var placed))
                transform.Position = placed;
            if (riseIn)
            {
                var rise = entityManager.GetComponentData<SpawnRise>(enemies[i]);
                rise.TargetY = transform.Position.y;
                entityManager.SetComponentData(enemies[i], rise);
                transform.Position.y -= riseDepth;
            }
            entityManager.SetComponentData(enemies[i], transform);
        }

        entityManager.DestroyEntity(template);
        return enemies.Length > 0 ? enemies[0] : Entity.Null;
    }

    [ContextMenu("Spawn test group")]
    void SpawnTestGroup()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Enter Play mode to spawn enemies.", this); return; }
        SpawnGroup(TestDefinition, NearPlayer(new Vector3(6f, 0f, 0f)), TestCount, 2f);
    }

    [ContextMenu("Spawn dummy")]
    void SpawnDummy()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Enter Play mode to spawn enemies.", this); return; }
        Spawn(DummyDefinition, NearPlayer(new Vector3(0f, 0f, 4f)));
    }

    // The player's position plus offset, or the manager's position when there is no player
    Vector3 NearPlayer(Vector3 offset)
    {
        var characterManager = CharacterManager.Instance;
        var world = World.DefaultGameObjectInjectionWorld;
        if (characterManager != null && world != null && world.EntityManager.Exists(characterManager.Player))
            return (Vector3)world.EntityManager.GetComponentData<LocalTransform>(characterManager.Player).Position + offset;
        return transform.position;
    }

    bool TryGetPrefabs(out EntityManager entityManager, out DynamicBuffer<EnemyPrefabElement> prefabs)
    {
        entityManager = default;
        prefabs = default;
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;

        entityManager = world.EntityManager;
        if (_queryWorld != world)
        {
            _prefabsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<EnemyPrefabElement>());
            _queryWorld = world;
        }
        if (_prefabsQuery.IsEmpty) return false;
        // Nothing spawns before a generated level exists
        if (!LevelGeneration.LevelState.IsReady(entityManager)) return false;

        prefabs = entityManager.GetBuffer<EnemyPrefabElement>(_prefabsQuery.GetSingletonEntity(), isReadOnly: true);
        return true;
    }

    static bool TryFindPrefab(DynamicBuffer<EnemyPrefabElement> prefabs, EnemyDefinition definition, out Entity prefab)
    {
        var key = new FixedString64Bytes(definition.name);
        foreach (var element in prefabs)
        {
            if (element.Key == key && element.Prefab != Entity.Null)
            {
                prefab = element.Prefab;
                return true;
            }
        }
        prefab = Entity.Null;
        return false;
    }
}
