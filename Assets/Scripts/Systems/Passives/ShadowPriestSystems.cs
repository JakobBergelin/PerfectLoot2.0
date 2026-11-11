using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Shadow Priest (see ShadowPriest). Between working out the damage and dealing it: every damage the wearer deals to an
/// enemy this frame (hits and damage over time, not their companions') is lowered by their Intelligence share, and that
/// share of what's left is gathered as healing. Then, for each wearer with healing gathered and no wait left, the healing
/// is split between them and every ally (player or companion) within Range, one dark orb each, flying from the enemy
/// that gave the most since the last orbs. So one attack hitting many enemies sends one orb per ally, and damage over
/// time is gathered for SendInterval.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateAfter(typeof(BrittleSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct ShadowPriestSystem : ISystem
{
    const float OrbRange = 60f;     // Meters an orb may fly before it gives up
    const float SelfArmTime = 0.7f; // Seconds before the wearer's own orb can land: long enough for its detour
    const float AllyArmTime = 0.2f; // ...and an ally's, so even one right next to the wearer sees it fly

    Random _random;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _random = Random.CreateFromIndex((uint)System.Environment.TickCount);
        state.RequireForUpdate<ShadowPriest>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    /// <summary>The share of Intelligence in Strength + Intelligence: what the damage loses and what of it heals. 0 without either.</summary>
    public static float IntelligenceShare(in StatBlock stats)
    {
        float total = math.max(0f, stats.Strength) + math.max(0f, stats.Intelligence);
        return total > 0f ? math.saturate(math.max(0f, stats.Intelligence) / total) : 0f;
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var priests = SystemAPI.GetComponentLookup<ShadowPriest>();
        var stats = SystemAPI.GetComponentLookup<CalculatedStats>(true);
        state.CompleteDependency();  // The lookups are used here on the main thread

        // Lower the wearers' damage and gather the healing, remembering the enemy that gave each the most
        foreach (var (taken, transform) in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>, RefRO<LocalTransform>>().WithAll<Enemy>())
        {
            for (int i = 0; i < taken.Length; i++)
            {
                ref var instance = ref taken.ElementAt(i);
                var source = instance.Source;
                // A companion's damage (its Source is its owner) isn't the wearer's
                if (instance.Damage <= 0f || instance.Origin.Unit != Entity.Null || !priests.HasComponent(source) || !stats.HasComponent(source)) continue;

                float share = IntelligenceShare(stats[source].Value);
                if (share <= 0f) continue;
                instance.Damage *= 1f - share;
                float heal = instance.Damage * share;
                if (heal <= 0f) continue;

                var priest = priests[source];
                priest.Pending += heal;
                if (heal > priest.OriginHeal)
                {
                    priest.OriginHeal = heal;
                    priest.Origin = transform.ValueRO.Position;
                }
                priests[source] = priest;
            }
        }

        SendOrbs(ref state);
    }

    // The gathered healing, split between the wearer and the allies near them, as one orb each
    void SendOrbs(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var allies = SystemAPI.QueryBuilder().WithAll<LocalTransform, Health>().WithAny<Player, Companion>().WithNone<RigidDeath>().Build();
        NativeArray<Entity> allyEntities = default;
        NativeArray<LocalTransform> allyTransforms = default;
        var inRange = new NativeList<int>(Allocator.Temp);
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (priestRW, transform, entity) in SystemAPI.Query<RefRW<ShadowPriest>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            ref var priest = ref priestRW.ValueRW;
            priest.Wait = math.max(0f, priest.Wait - deltaTime);
            if (priest.Pending <= 0f || priest.Wait > 0f) continue;

            if (!allyEntities.IsCreated)
            {
                allyEntities = allies.ToEntityArray(Allocator.Temp);
                allyTransforms = allies.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            }
            var wearer = transform.ValueRO.Position;
            inRange.Clear();
            for (int i = 0; i < allyEntities.Length; i++)
                if (allyEntities[i] == entity || math.distance(allyTransforms[i].Position.xz, wearer.xz) <= priest.Range) inRange.Add(i);
            if (inRange.IsEmpty) continue;

            float each = priest.Pending / inRange.Length;
            var origin = priest.OriginHeal > 0f ? priest.Origin : wearer;
            foreach (int i in inRange)
            {
                bool self = allyEntities[i] == entity;
                // The wearer's own orb: out a random way, turning slowly at first; an ally's: straight there
                float3 direction;
                float turnSpeed, turnAcceleration;
                if (self)
                {
                    float angle = _random.NextFloat(2f * math.PI);
                    direction = new float3(math.cos(angle), 0f, math.sin(angle));
                    turnSpeed = 1f;
                    turnAcceleration = 6f;
                }
                else
                {
                    direction = math.normalizesafe(new float3(allyTransforms[i].Position.x - origin.x, 0f, allyTransforms[i].Position.z - origin.z), new float3(0f, 0f, 1f));
                    turnSpeed = 4f;
                    turnAcceleration = 8f;
                }

                var orb = commandBuffer.CreateEntity();
                commandBuffer.AddComponent(orb, new LocalTransform { Position = origin, Rotation = quaternion.LookRotationSafe(direction, math.up()), Scale = 1f });
                commandBuffer.AddComponent(orb, new Projectile { Direction = direction, Speed = priest.ProjectileSpeed, FollowGround = true, PassThroughWalls = true });
                commandBuffer.AddComponent(orb, new HomingProjectile { Target = allyEntities[i], TurnSpeed = turnSpeed, TurnAcceleration = turnAcceleration });
                commandBuffer.AddComponent(orb, new ProjectileRange { MaxDistance = OrbRange, LastPosition = origin });
                commandBuffer.AddComponent(orb, new HealOnArrival { Target = allyEntities[i], Amount = each, Label = DamageLabel.ShadowPriest, ArmedIn = self ? SelfArmTime : AllyArmTime });
                commandBuffer.AddComponent(orb, new Source { Entity = entity });
            }

            priest.Pending = 0f;
            priest.OriginHeal = 0f;
            priest.Wait = priest.SendInterval;
        }
    }
}
