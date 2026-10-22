using Enemies;
using Unity.Collections;
using Unity.Entities;
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
    public Entity Spawn(EnemyDefinition definition, Vector3 position)
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

        var enemy = entityManager.Instantiate(prefab);
        var transform = entityManager.GetComponentData<LocalTransform>(enemy);
        transform.Position = position;
        entityManager.SetComponentData(enemy, transform);

        EnemyEntitySetup.AddGameplayComponents(entityManager, enemy, definition);
        return enemy;
    }

    /// <summary>Spawns count enemies at random points within radius of center (on the same height).</summary>
    public void SpawnGroup(EnemyDefinition definition, Vector3 center, int count, float radius)
    {
        for (int i = 0; i < count; i++)
        {
            var offset = Random.insideUnitCircle * radius;
            if (Spawn(definition, center + new Vector3(offset.x, 0f, offset.y)) == Entity.Null) return;
        }
    }

    [ContextMenu("Spawn test group")]
    void SpawnTestGroup()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Enter Play mode to spawn enemies.", this); return; }

        // A bit away from the player, or around the manager when there is no player
        var center = transform.position;
        var characterManager = CharacterManager.Instance;
        var world = World.DefaultGameObjectInjectionWorld;
        if (characterManager != null && world != null && world.EntityManager.Exists(characterManager.Player))
            center = (Vector3)world.EntityManager.GetComponentData<LocalTransform>(characterManager.Player).Position + new Vector3(6f, 0f, 0f);

        SpawnGroup(TestDefinition, center, TestCount, 2f);
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
