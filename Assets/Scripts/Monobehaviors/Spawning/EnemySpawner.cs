using System;
using System.Collections.Generic;
using System.Text;
using Enemies;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Spawns a predefined group of enemies once, when the player first comes within TriggerRadius. Place it on the ground
/// (its position is the ground point); the enemies rise out of the ground around it. Uses EnemyManager.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public EnemyDefinition Definition;
        [Min(1)] public int Count = 1;
    }

    [Tooltip("Horizontal distance from the player that triggers the spawn; keep it beyond the screen edge")]
    public float TriggerRadius = 35f;
    [Tooltip("How far around this point the group is scattered")]
    public float SpawnRadius = 4f;
    public List<Entry> Group = new List<Entry>();

    bool _spawned;

    void Update()
    {
        if (_spawned) return;

        var enemyManager = EnemyManager.Instance;
        var characterManager = CharacterManager.Instance;
        var world = World.DefaultGameObjectInjectionWorld;
        if (enemyManager == null || !enemyManager.IsReady || characterManager == null || world == null) return;

        var entityManager = world.EntityManager;
        var player = characterManager.Player;
        if (player == Entity.Null || !entityManager.Exists(player) || !entityManager.HasComponent<LocalTransform>(player)) return;

        Vector3 playerPosition = entityManager.GetComponentData<LocalTransform>(player).Position;
        var offset = playerPosition - transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude > TriggerRadius * TriggerRadius) return;

        _spawned = true;
        foreach (var entry in Group)
        {
            if (entry.Definition == null || entry.Count <= 0) continue;
            // This point is the ground: the capsule's center is half its height (1 x scale) above it
            var center = transform.position + Vector3.up * entry.Definition.Scale;
            enemyManager.SpawnGroup(entry.Definition, center, entry.Count, SpawnRadius, riseIn: true);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        var position = transform.position;
        UnityEditor.Handles.color = _spawned ? new Color(0.5f, 0.5f, 0.5f, 0.6f) : new Color(1f, 0.85f, 0.2f, 0.9f);
        UnityEditor.Handles.DrawWireDisc(position, Vector3.up, TriggerRadius);
        UnityEditor.Handles.color = new Color(1f, 0.3f, 0.3f, 0.9f);
        UnityEditor.Handles.DrawWireDisc(position, Vector3.up, SpawnRadius);

        var label = new StringBuilder(name);
        foreach (var entry in Group)
            if (entry.Definition != null) label.Append($"\n{entry.Count}x {entry.Definition.DisplayName}");
        UnityEditor.Handles.Label(position + Vector3.up * 2f, label.ToString());
    }
#endif
}
