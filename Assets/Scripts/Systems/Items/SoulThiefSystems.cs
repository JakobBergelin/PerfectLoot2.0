using Enemies;
using Passives;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Soul Thief legendary power, added by SoulThiefPower while the item is equipped.</summary>
public struct SoulThief : IComponentData
{
    public float SoulLifetime;  // Seconds a soul stays on the ground
    public float PickupRadius;  // Meters from the wearer a soul is taken at
    public float Duration;      // Seconds a stolen passive lasts, scaled by BuffDuration
    public int MaxSouls;        // Different passives held at once; a new one replaces the oldest
}

/// <summary>A soul on the ground, its own entity at the spot its enemy died.</summary>
public struct Soul : IComponentData
{
    public UnityObjectRef<PassiveSkill> Passive;
    public float TimeRemaining;
}

/// <summary>A passive taken from a soul, oldest first. SoulThiefSystem ticks it, with or without the power.</summary>
public struct StolenPassive : IBufferElementData
{
    public UnityObjectRef<PassiveSkill> Passive;
    public float TimeRemaining;
    public float Duration;
}

/// <summary>
/// While anyone wears Soul Thief, an enemy that dies with passives drops a soul holding one of them at random, whatever
/// killed it. Before CheckDeathSystem removes the dead. Not an ISystem, as it reads the EnemyDefinition.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateBefore(typeof(CheckDeathSystem))]
public partial class SoulDropSystem : SystemBase
{
    protected override void OnCreate() => RequireForUpdate<SoulThief>();

    protected override void OnUpdate()
    {
        float lifetime = 0f;
        foreach (var thief in SystemAPI.Query<RefRO<SoulThief>>())
            lifetime = math.max(lifetime, thief.ValueRO.SoulLifetime);

        var souls = new NativeList<(Soul, float3)>(Allocator.Temp);
        foreach (var (health, transform, info) in SystemAPI.Query<RefRO<Health>, RefRO<LocalTransform>, RefRO<UnitInfo>>()
                     .WithAll<Enemy>().WithNone<DestroyWithChildren>())
        {
            if (health.ValueRO.CurrentValue > 0f) continue;
            var definition = info.ValueRO.Source.Value as EnemyDefinition;
            if (definition == null || definition.Passives.Count == 0) continue;

            var passive = definition.Passives[UnityEngine.Random.Range(0, definition.Passives.Count)];
            souls.Add((new Soul { Passive = passive, TimeRemaining = lifetime }, transform.ValueRO.Position));
        }

        // After the loop: creating entities is a structural change
        foreach (var (soul, position) in souls)
        {
            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, soul);
            EntityManager.AddComponentData(entity, LocalTransform.FromPosition(position));
        }
    }
}

/// <summary>
/// Walking over a soul steals its passive for Duration, restarting the timer if it's already held. A passive that's
/// equipped in a slot can't be stolen, so running out never takes it away. Runs without wearers too, so souls and stolen
/// passives run out after the item comes off. Not an ISystem, as passives are assets.
/// </summary>
[UpdateInGroup(typeof(EffectSystemGroup))]
public partial class SoulThiefSystem : SystemBase
{
    protected override void OnUpdate()
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        var expired = new NativeList<(Entity, UnityObjectRef<PassiveSkill>)>(Allocator.Temp);
        foreach (var (stolen, entity) in SystemAPI.Query<DynamicBuffer<StolenPassive>>().WithEntityAccess())
        {
            for (int i = stolen.Length - 1; i >= 0; i--)
            {
                ref var entry = ref stolen.ElementAt(i);
                entry.TimeRemaining -= deltaTime;
                if (entry.TimeRemaining > 0f) continue;
                expired.Add((entity, entry.Passive));
                stolen.RemoveAt(i);
            }
        }

        var taken = new NativeList<(Entity, UnityObjectRef<PassiveSkill>)>(Allocator.Temp);
        var gone = new NativeList<Entity>(Allocator.Temp);
        foreach (var (soul, transform, entity) in SystemAPI.Query<RefRW<Soul>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            soul.ValueRW.TimeRemaining -= deltaTime;
            var thief = Equipped(soul.ValueRO.Passive.Value) ? Entity.Null : FindThief(transform.ValueRO.Position);
            if (thief != Entity.Null) taken.Add((thief, soul.ValueRO.Passive));
            if (thief != Entity.Null || soul.ValueRO.TimeRemaining <= 0f) gone.Add(entity);
        }

        // After the loops: Apply and Remove are structural changes
        foreach (var (owner, passive) in expired) Release(owner, passive.Value);
        foreach (var (thief, passive) in taken) Steal(thief, passive.Value);
        EntityManager.DestroyEntity(gone.AsArray());
    }

    Entity FindThief(float3 position)
    {
        foreach (var (thief, transform, entity) in SystemAPI.Query<RefRO<SoulThief>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            float radius = thief.ValueRO.PickupRadius;
            if (math.distancesq(position.xz, transform.ValueRO.Position.xz) <= radius * radius) return entity;
        }
        return Entity.Null;
    }

    void Steal(Entity thief, PassiveSkill passive)
    {
        var power = EntityManager.GetComponentData<SoulThief>(thief);
        float duration = EffectApply.BuffDuration(power.Duration, EntityManager.GetComponentData<CalculatedStats>(thief).Value);
        var stolen = EntityManager.GetBuffer<StolenPassive>(thief);

        int held = IndexOf(stolen, passive);
        PassiveSkill replaced = null;
        if (held >= 0) stolen.RemoveAt(held);  // Added again below, so it counts as the newest
        else if (stolen.Length >= power.MaxSouls)
        {
            replaced = stolen[0].Passive.Value;
            stolen.RemoveAt(0);
        }
        stolen.Add(new StolenPassive { Passive = passive, TimeRemaining = duration, Duration = duration });

        if (replaced != null) Release(thief, replaced);
        if (held < 0) passive.Apply(EntityManager, thief);
    }

    // It may have been equipped in a slot since it was stolen
    void Release(Entity owner, PassiveSkill passive)
    {
        if (!Equipped(passive)) passive.Remove(EntityManager, owner);
    }

    static int IndexOf(DynamicBuffer<StolenPassive> stolen, PassiveSkill passive)
    {
        for (int i = 0; i < stolen.Length; i++)
            if (stolen[i].Passive.Value == passive) return i;
        return -1;
    }

    static bool Equipped(PassiveSkill passive)
    {
        var manager = PassiveSkillManager.Instance;
        return manager != null && manager.Active.Contains(passive);
    }
}
