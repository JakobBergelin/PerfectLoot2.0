using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>Split passive: the enemy it turns into when it dies, and how many.</summary>
public struct Split : IComponentData
{
    public UnityObjectRef<Enemies.EnemyDefinition> Into;
    public int Count;
    public float Spread;    // Meters from where it died
}

/// <summary>An enemy that came out of a Split: it counts as a kill but doesn't roll the kill loot table (DungeonManager).</summary>
public struct NoKillLoot : IComponentData {}

/// <summary>A Split enemy whose split already happened (dying enemies can stay at 0 health for a while).</summary>
public struct SplitDone : IComponentData {}

/// <summary>
/// On the host, an enemy with Split that reached 0 health spawns Count of its Into enemy, evenly around where it died,
/// each marked NoKillLoot. Before CheckDeathSystem, which removes the dead.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateBefore(typeof(CheckDeathSystem))]
public partial class SplitSystem : SystemBase
{
    protected override void OnCreate() => RequireForUpdate<Split>();

    protected override void OnUpdate()
    {
        var manager = EnemyManager.Instance;
        if (manager == null) return;
        var dead = new NativeList<Entity>(Allocator.Temp);
        var splits = new NativeList<Split>(Allocator.Temp);
        var positions = new NativeList<float3>(Allocator.Temp);
        foreach (var (split, health, transform, entity) in SystemAPI.Query<RefRO<Split>, RefRO<Health>, RefRO<LocalTransform>>()
                     .WithNone<SplitDone>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue > 0f) continue;
            dead.Add(entity);
            splits.Add(split.ValueRO);
            positions.Add(transform.ValueRO.Position);
        }
        if (dead.IsEmpty) return;
        EntityManager.AddComponent<SplitDone>(dead.AsArray());

        for (int i = 0; i < dead.Length; i++)
        {
            var split = splits[i];
            var definition = split.Into.Value;
            if (definition == null) continue;
            float turn = (dead[i].Index % 7) * 0.9f;   // Not every split lands the same way round
            for (int n = 0; n < split.Count; n++)
            {
                float angle = turn + n * 2f * math.PI / math.max(1, split.Count);
                var offset = new float3(math.cos(angle), 0f, math.sin(angle)) * split.Spread;
                var spawned = manager.Spawn(definition, (Vector3)(positions[i] + offset));
                if (spawned != Entity.Null) EntityManager.AddComponent<NoKillLoot>(spawned);
            }
        }
    }
}
