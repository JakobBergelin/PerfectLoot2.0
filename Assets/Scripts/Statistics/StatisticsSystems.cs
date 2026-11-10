using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>Something to count, found by the statistics systems; StatisticsRecorder adds them to the account each frame.</summary>
public struct StatEvent : IBufferElementData
{
    public StatType Type;
    public UnityObjectRef<ScriptableObject> Subject;   // The enemy (its EnemyDefinition), or none
    public double Amount;
    public int Player;      // The local player it counts for (PlayerIndex), or Everyone

    public const int Everyone = -1;
}

/// <summary>A dead unit whose death was counted (dying units can stay at 0 health for a while).</summary>
public struct DeathCounted : IComponentData
{
}

/// <summary>On players: the experience they had in total when last looked at, so only what they earn is counted.</summary>
public struct ExperienceCounted : IComponentData
{
    public int Total;
}

// The statistics systems only read what other systems did. Everything they count goes through StatEvent, which only
// StatisticsRecorder reads. Damage dealt per skill isn't here: the recorder reads the Damage Meter's totals instead.

/// <summary>
/// Counts every enemy that died this frame (and bosses), for every player, and every player that died with the enemy
/// that hit it last, for that player.
/// Runs just before CheckDeathSystem, which removes dead units.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateBefore(typeof(CheckDeathSystem))]
public partial struct DeathStatisticsSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.CreateSingletonBuffer<StatEvent>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var events = SystemAPI.GetSingletonBuffer<StatEvent>();
        var bosses = SystemAPI.GetComponentLookup<DungeonBoss>(true);
        var infos = SystemAPI.GetComponentLookup<UnitInfo>(true);
        var counted = new NativeList<Entity>(Allocator.Temp);

        foreach (var (health, info, entity) in SystemAPI.Query<RefRO<Health>, RefRO<UnitInfo>>()
                     .WithAll<Enemy>().WithNone<DeathCounted>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue > 0f) continue;
            counted.Add(entity);
            events.Add(new StatEvent { Type = StatType.EnemiesKilled, Subject = info.ValueRO.Source, Amount = 1, Player = StatEvent.Everyone });
            if (bosses.HasComponent(entity))
                events.Add(new StatEvent { Type = StatType.BossesKilled, Subject = info.ValueRO.Source, Amount = 1, Player = StatEvent.Everyone });
        }

        var lastHits = SystemAPI.GetComponentLookup<LastHit>(true);
        var playerIndices = SystemAPI.GetComponentLookup<PlayerIndex>(true);
        foreach (var (health, entity) in SystemAPI.Query<RefRO<Health>>().WithAll<Player>().WithNone<DeathCounted>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue > 0f) continue;
            counted.Add(entity);
            // The killer can be gone already (e.g. a projectile of an enemy that died): then only the death counts
            var killer = lastHits.HasComponent(entity) ? lastHits[entity].Source : Entity.Null;
            var subject = infos.HasComponent(killer) ? infos[killer].Source : default;
            int player = playerIndices.HasComponent(entity) ? playerIndices[entity].Value : 0;
            events.Add(new StatEvent { Type = StatType.Deaths, Subject = subject, Amount = 1, Player = player });
        }

        if (!counted.IsEmpty) state.EntityManager.AddComponent<DeathCounted>(counted.AsArray());
    }
}

/// <summary>
/// Counts the experience players earn: how much their total (all levels so far plus the current experience) grew.
/// A new player entity (spawn, respawn, new scene) starts from what it has.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(ExperienceSystem))]
public partial struct ExperienceStatisticsSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StatEvent>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var events = SystemAPI.GetSingletonBuffer<StatEvent>();
        foreach (var (level, seen, player) in SystemAPI.Query<RefRO<Level>, RefRW<ExperienceCounted>, RefRO<PlayerIndex>>().WithAll<Player>())
        {
            int total = TotalOf(level.ValueRO);
            if (total > seen.ValueRO.Total)
                events.Add(new StatEvent { Type = StatType.ExperienceEarned, Amount = total - seen.ValueRO.Total, Player = player.ValueRO.Value });
            seen.ValueRW.Total = total;
        }

        // Players seen for the first time
        var fresh = new NativeList<Entity>(Allocator.Temp);
        var totals = new NativeList<int>(Allocator.Temp);
        foreach (var (level, entity) in SystemAPI.Query<RefRO<Level>>().WithAll<Player>().WithNone<ExperienceCounted>().WithEntityAccess())
        {
            fresh.Add(entity);
            totals.Add(TotalOf(level.ValueRO));
        }
        for (int i = 0; i < fresh.Length; i++)
            state.EntityManager.AddComponentData(fresh[i], new ExperienceCounted { Total = totals[i] });
    }

    static int TotalOf(in Level level)
    {
        int total = level.Experience;
        for (int l = 1; l < level.Value; l++) total += Experience.ToNextLevel(l);
        return total;
    }
}
