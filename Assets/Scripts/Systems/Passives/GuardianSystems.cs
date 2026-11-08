using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Guardian, just before damage is dealt:
/// 1. Every ally (player or companion, not a Guardian itself) within Radius of a Guardian passes Share of each hit it
///    takes to the nearest one. The Guardian takes it as sub-damage (GuardianShare): no armor again, no status. Worked out
///    from this frame's hits first and added after, so Guardians never pass damage back and forth.
/// 2. Everything a Guardian takes this frame fills its Charge. Full: Consecration at its feet, an instant heal for allies
///    and a physical blast for enemies, both within ConsecrationRadius.
/// After the systems that change damage taken (Borrowed Time is the last of them), so only what's left is shared.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(BorrowedTimeSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct GuardianSystem : ISystem
{
    struct InRange { public Entity Entity; public float3 Position; public float Radius; public float Share; }
    struct Passed { public Entity Guardian; public CalculatedDamageInstance Instance; }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Guardian>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var guardians = new NativeList<InRange>(Allocator.Temp);
        foreach (var (guardian, transform, entity) in SystemAPI.Query<RefRO<Guardian>, RefRO<LocalTransform>>().WithEntityAccess())
            guardians.Add(new InRange { Entity = entity, Position = transform.ValueRO.Position, Radius = guardian.ValueRO.Radius, Share = guardian.ValueRO.Share });

        // 1. Allies pass a share of their hits to the nearest Guardian in range
        var passed = new NativeList<Passed>(Allocator.Temp);
        foreach (var (taken, transform, entity) in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>, RefRO<LocalTransform>>()
                     .WithAny<Player, Companion>().WithNone<Guardian>().WithEntityAccess())
        {
            if (taken.IsEmpty) continue;
            int nearest = -1;
            float nearestDistance = float.MaxValue;
            for (int g = 0; g < guardians.Length; g++)
            {
                float distance = math.distance(guardians[g].Position.xz, transform.ValueRO.Position.xz);
                if (distance <= guardians[g].Radius && distance < nearestDistance) { nearest = g; nearestDistance = distance; }
            }
            if (nearest < 0) continue;

            for (int i = 0; i < taken.Length; i++)
            {
                ref var instance = ref taken.ElementAt(i);
                if (instance.Damage <= 0f) continue;
                float share = instance.Damage * guardians[nearest].Share;
                instance.Damage -= share;
                passed.Add(new Passed { Guardian = guardians[nearest].Entity, Instance = new CalculatedDamageInstance
                {
                    Damage = share,
                    Type = instance.Type,
                    Hierarchy = DamageHierarchy.Sub,
                    Source = instance.Source,
                    Origin = DamageLabel.GuardianShare,
                } });
            }
        }
        var buffers = SystemAPI.GetBufferLookup<CalculatedDamageInstance>();
        foreach (var pass in passed)
            if (buffers.HasBuffer(pass.Guardian)) buffers[pass.Guardian].Add(pass.Instance);

        // 2. What a Guardian takes fills Consecration
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        foreach (var (guardian, taken, transform, health, entity) in
                 SystemAPI.Query<RefRW<Guardian>, DynamicBuffer<CalculatedDamageInstance>, RefRO<LocalTransform>, RefRO<Health>>().WithEntityAccess())
        {
            ref var g = ref guardian.ValueRW;
            foreach (var instance in taken) g.Charge += math.max(0f, instance.Damage);
            if (g.ConsecrationThreshold <= 0f || g.Charge < g.ConsecrationThreshold) continue;
            g.Charge = 0f;

            var position = transform.ValueRO.Position;
            var heal = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(heal, new LocalTransform { Position = position, Rotation = quaternion.identity, Scale = 1f });
            commandBuffer.AddComponent(heal, new Heal { Label = DamageLabel.Consecration, Value = health.ValueRO.MaxValue * g.ConsecrationHealShare, Type = TargetType.Player, Radius = g.ConsecrationRadius });
            commandBuffer.AddComponent(heal, new Source { Entity = entity });
            commandBuffer.AddComponent(heal, new InitializeFromSource());
            commandBuffer.AddComponent(heal, new DestroyWithChildren());   // A single-frame heal

            var blast = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(blast, new LocalTransform { Position = position, Rotation = quaternion.identity, Scale = 1f });
            commandBuffer.AddComponent(blast, new Attack { Origin = DamageLabel.Consecration, Damage = g.ConsecrationDamage, Type = DamageType.Physical, Radius = g.ConsecrationRadius, DamageHierarchy = DamageHierarchy.Main });
            commandBuffer.AddComponent(blast, new Source { Entity = entity });
            commandBuffer.AddComponent(blast, new AttackTarget { TargetType = TargetType.Enemy });
            commandBuffer.AddComponent(blast, new InitializeFromSource());
            commandBuffer.AddComponent(blast, new DestroyWithChildren());
        }
    }
}

/// <summary>Guardian: ArmorPerAlly for each ally (player or companion) within Radius.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct GuardianStatsSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Guardian>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var allies = SystemAPI.QueryBuilder().WithAll<LocalTransform>().WithAny<Player, Companion>().WithNone<RigidDeath>().Build();
        var entities = allies.ToEntityArray(Allocator.Temp);
        var transforms = allies.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        foreach (var (guardian, transform, stats, entity) in SystemAPI.Query<RefRO<Guardian>, RefRO<LocalTransform>, RefRW<CalculatedStats>>().WithEntityAccess())
        {
            int near = 0;
            for (int i = 0; i < entities.Length; i++)
                if (entities[i] != entity && math.distance(transforms[i].Position.xz, transform.ValueRO.Position.xz) <= guardian.ValueRO.Radius) near++;
            stats.ValueRW.Value.Armor += near * guardian.ValueRO.ArmorPerAlly;
        }
    }
}
