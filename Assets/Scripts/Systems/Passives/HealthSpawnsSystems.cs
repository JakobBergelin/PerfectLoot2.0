using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>Health Spawns passive: once the enemy's health drops to HealthBelow of its max, Count of another enemy appear around it.</summary>
[InternalBufferCapacity(2)]
public struct HealthSpawn : IBufferElementData
{
    public UnityObjectRef<Enemies.EnemyDefinition> Into;
    public float HealthBelow;   // Share of max health, 0-1
    public int Count;
    public float Spread;        // Meters from the enemy
    public bool Done;
}

/// <summary>
/// On the host, each HealthSpawn of a living enemy goes off once, the first time its health is at or below HealthBelow:
/// Count of its Into enemy, evenly around it, each marked NoKillLoot. A big hit past two thresholds sets off both.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateBefore(typeof(CheckDeathSystem))]
public partial class HealthSpawnsSystem : SystemBase
{
    protected override void OnCreate() => RequireForUpdate<HealthSpawn>();

    protected override void OnUpdate()
    {
        var manager = EnemyManager.Instance;
        if (manager == null) return;
        var spawns = new NativeList<HealthSpawn>(Allocator.Temp);
        var positions = new NativeList<float3>(Allocator.Temp);
        foreach (var (buffer, health, transform) in SystemAPI.Query<DynamicBuffer<HealthSpawn>, RefRO<Health>, RefRO<LocalTransform>>())
        {
            var h = health.ValueRO;
            if (h.CurrentValue <= 0f) continue;
            for (int i = 0; i < buffer.Length; i++)
            {
                ref var spawn = ref buffer.ElementAt(i);
                if (spawn.Done || h.CurrentValue > h.MaxValue * spawn.HealthBelow) continue;
                spawn.Done = true;
                spawns.Add(spawn);
                positions.Add(transform.ValueRO.Position);
            }
        }

        for (int i = 0; i < spawns.Length; i++)
        {
            var spawn = spawns[i];
            var definition = spawn.Into.Value;
            if (definition == null) continue;
            float turn = i * 0.9f;   // Not every wave lands the same way round
            for (int n = 0; n < spawn.Count; n++)
            {
                float angle = turn + n * 2f * math.PI / math.max(1, spawn.Count);
                var offset = new float3(math.cos(angle), 0f, math.sin(angle)) * spawn.Spread;
                var spawned = manager.Spawn(definition, (Vector3)(positions[i] + offset));
                if (spawned != Entity.Null) EntityManager.AddComponent<NoKillLoot>(spawned);
            }
        }
    }
}
