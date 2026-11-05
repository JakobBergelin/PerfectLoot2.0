using Enemies;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Raise Thrall: pressing its key switches it on or off, even while another skill is held, and its slot stays lit while
/// it's on. Marks go when their owner switches it off, and Thralls when their owner dies or unequips it.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct RaiseThrallSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var raises = SystemAPI.GetComponentLookup<RaiseThrall>(true);
        var healths = SystemAPI.GetComponentLookup<Health>(true);
        state.CompleteDependency();  // The lookups are used here on the main thread

        // Never enabled: the key is read here, so that a skill in a lower slot can't hold it back
        foreach (var (raise, inputs, modifiers, skills) in SystemAPI.Query<RefRW<RaiseThrall>, RefRO<InputsData>, RefRO<MovementModifiers>, DynamicBuffer<Skill>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (!SkillUtil.FindSkill(skills, SkillType.RaiseThrall, ref index)) continue;

            int slot = skills[index].SlotIndex;
            if (slot >= 0 && slot < 4 && inputs.ValueRO.SkillPressedThisFrame[slot] && !modifiers.ValueRO.PreventsActions)
            {
                raise.ValueRW.Active = !raise.ValueRO.Active;
                SkillUtil.MarkUsed(skills, SkillType.RaiseThrall);
            }
            skills.ElementAt(index).State.IsActive = raise.ValueRO.Active;
        }

        foreach (var (mark, entity) in SystemAPI.Query<RefRO<ThrallMark>>().WithEntityAccess())
        {
            var owner = mark.ValueRO.Owner;
            if (!raises.HasComponent(owner) || !raises[owner].Active) SystemAPI.SetComponentEnabled<ThrallMark>(entity, false);
        }

        foreach (var (companion, entity) in SystemAPI.Query<RefRO<Companion>>().WithAll<Thrall>().WithNone<DestroyWithChildren, RigidDeath>().WithEntityAccess())
        {
            var owner = companion.ValueRO.Owner;
            bool ownerAlive = healths.HasComponent(owner) && healths[owner].CurrentValue > 0f;
            if (!raises.HasComponent(owner) || !ownerAlive) commandBuffer.AddComponent<DestroyWithChildren>(entity);
        }
    }
}

/// <summary>Takes the penalty off critical chance while Raise Thrall is on.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct RaiseThrallStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (raise, stats) in SystemAPI.Query<RefRO<RaiseThrall>, RefRW<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (raise.ValueRO.Active) stats.ValueRW.Value.CriticalChance -= raise.ValueRO.CriticalChancePenalty;
        }
    }
}

/// <summary>
/// Marks every enemy hit by someone with Raise Thrall on, companions' hits included. Only enemies that use skills, as a
/// Thrall needs them: not Swarm enemies or training dummies. Before the damage is dealt, so a killing blow marks it too.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct ThrallMarkSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<RaiseThrall>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var raises = SystemAPI.GetComponentLookup<RaiseThrall>(true);

        foreach (var (taken, entity) in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>().WithAll<Enemy, EnemySkillCast>().WithPresent<ThrallMark>().WithEntityAccess())
        {
            foreach (var instance in taken)
            {
                if (!raises.HasComponent(instance.Source) || !raises[instance.Source].Active) continue;

                float duration = raises[instance.Source].MarkDuration;
                SystemAPI.SetComponent(entity, new ThrallMark { TimeRemaining = duration, Duration = duration, Owner = instance.Source });
                SystemAPI.SetComponentEnabled<ThrallMark>(entity, true);
            }
        }
    }
}

/// <summary>
/// A marked enemy that died rises as its marker's Thrall where it fell, and the oldest Thrall dies if that's one too many.
/// After the damage is dealt and before CheckDeathSystem removes the dead, while they still have the mark. Not an ISystem,
/// as it reads the EnemyDefinition.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(DamagingSystem))]
[UpdateBefore(typeof(CheckDeathSystem))]
public partial class ThrallRaiseSystem : SystemBase
{
    struct Fallen
    {
        public Entity Owner;
        public LocalTransform Transform;
        public UnityObjectRef<ScriptableObject> Source;
    }

    protected override void OnCreate() => RequireForUpdate<MovementConfig>();

    protected override void OnUpdate()
    {
        var fallen = new NativeList<Fallen>(Allocator.Temp);
        foreach (var (mark, health, transform, info, entity) in SystemAPI.Query<RefRO<ThrallMark>, RefRO<Health>, RefRO<LocalTransform>, RefRO<UnitInfo>>()
                     .WithAll<Enemy>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue > 0f) continue;
            SystemAPI.SetComponentEnabled<ThrallMark>(entity, false);
            fallen.Add(new Fallen { Owner = mark.ValueRO.Owner, Transform = transform.ValueRO, Source = info.ValueRO.Source });
        }

        // After the loop: raising is a structural change
        foreach (var unit in fallen) Raise(unit);
    }

    void Raise(Fallen unit)
    {
        var manager = EnemyManager.Instance;
        var definition = unit.Source.Value as EnemyDefinition;
        if (!SystemAPI.HasComponent<RaiseThrall>(unit.Owner) || manager == null || definition == null) return;
        var raise = SystemAPI.GetComponent<RaiseThrall>(unit.Owner);
        if (!raise.Active || !manager.TryFindBody(definition, out var body)) return;

        KillOldestIfFull(unit.Owner, raise.MaxThralls);

        var thrall = EntityManager.Instantiate(body);
        CompanionEntitySetup.AddThrallComponents(EntityManager, thrall, unit.Owner, definition, raise, SystemAPI.GetSingleton<MovementConfig>().MovementSpeed, SystemAPI.Time.ElapsedTime);

        // A body height under the spot; it doesn't move or act until risen (SpawnRiseSystem)
        float depth = 2f * Mathf.Max(0.01f, definition.Scale);
        var transform = unit.Transform;
        EntityManager.AddComponentData(thrall, new SpawnRise { TargetY = transform.Position.y, Depth = depth, Duration = manager.RiseDuration });
        transform.Position.y -= depth;
        EntityManager.SetComponentData(thrall, transform);
    }

    void KillOldestIfFull(Entity owner, int maxThralls)
    {
        int count = 0;
        var oldest = Entity.Null;
        double oldestTime = double.MaxValue;
        foreach (var (thrall, companion, health, entity) in SystemAPI.Query<RefRO<Thrall>, RefRO<Companion>, RefRO<Health>>().WithEntityAccess())
        {
            if (companion.ValueRO.Owner != owner || health.ValueRO.CurrentValue <= 0f) continue;
            count++;
            if (thrall.ValueRO.RaisedAt >= oldestTime) continue;
            oldestTime = thrall.ValueRO.RaisedAt;
            oldest = entity;
        }
        if (count < maxThralls || oldest == Entity.Null) return;

        var oldestHealth = SystemAPI.GetComponent<Health>(oldest);
        oldestHealth.CurrentValue = 0f;
        SystemAPI.SetComponent(oldest, oldestHealth);
    }
}
