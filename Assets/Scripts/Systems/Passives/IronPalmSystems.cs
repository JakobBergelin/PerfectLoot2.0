using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Iron Palm: every hit the wearer dodged or blocked this frame (HitsAvoided) stores a Ki. At KiNeeded it sends a healing
/// orb (HealOnArrival, homing) to itself and every ally (player or companion) within Range. Allies' orbs head straight for
/// them; the wearer's own leaves in a random direction and turns slowly at first, so it loops out before coming back.
/// After the damage is worked out (when the dodges and blocks are counted).
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[BurstCompile]
public partial struct IronPalmSystem : ISystem
{
    const float OrbRange = 60f;     // Meters an orb may fly before it gives up
    const float SelfArmTime = 0.7f; // Seconds before the wearer's own orb can land: long enough for its detour
    const float AllyArmTime = 0.2f; // ...and an ally's, so even one right next to the wearer sees it fly

    Random _random;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _random = Random.CreateFromIndex((uint)System.Environment.TickCount);
        state.RequireForUpdate<IronPalm>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var allies = SystemAPI.QueryBuilder().WithAll<LocalTransform, Health>().WithAny<Player, Companion>().WithNone<RigidDeath>().Build();
        NativeArray<Entity> allyEntities = default;
        NativeArray<LocalTransform> allyTransforms = default;

        foreach (var (palm, avoided, transform, entity) in SystemAPI.Query<RefRW<IronPalm>, RefRO<HitsAvoided>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            ref var p = ref palm.ValueRW;
            p.Ki += avoided.ValueRO.Count;
            if (p.KiNeeded <= 0 || p.Ki < p.KiNeeded) continue;
            p.Ki = 0;

            if (!allyEntities.IsCreated)
            {
                allyEntities = allies.ToEntityArray(Allocator.Temp);
                allyTransforms = allies.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            }
            var origin = transform.ValueRO.Position;
            for (int i = 0; i < allyEntities.Length; i++)
            {
                bool self = allyEntities[i] == entity;
                if (!self && math.distance(allyTransforms[i].Position.xz, origin.xz) > p.Range) continue;

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
                commandBuffer.AddComponent(orb, new Projectile { Direction = direction, Speed = p.ProjectileSpeed, FollowGround = true, PassThroughWalls = true });
                commandBuffer.AddComponent(orb, new HomingProjectile { Target = allyEntities[i], TurnSpeed = turnSpeed, TurnAcceleration = turnAcceleration });
                commandBuffer.AddComponent(orb, new ProjectileRange { MaxDistance = OrbRange, LastPosition = origin });
                commandBuffer.AddComponent(orb, new HealOnArrival { Target = allyEntities[i], HealShare = p.HealShare, Label = DamageLabel.IronPalm, ArmedIn = self ? SelfArmTime : AllyArmTime });
                commandBuffer.AddComponent(orb, new Source { Entity = entity });
            }
        }
    }
}

/// <summary>
/// Healing orbs (Iron Palm's, Shadow Priest's): one that reaches its target (within ArriveDistance, flat, once ArmedIn has
/// run out) heals it by HealShare of its max health plus Amount, and goes away; one whose target is gone goes away too
/// (its homing would otherwise pick an enemy).
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(ProjectileSystem))]
[BurstCompile]
public partial struct HealOnArrivalSystem : ISystem
{
    const float ArriveDistance = 0.8f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<HealOnArrival>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var transforms = SystemAPI.GetComponentLookup<LocalTransform>(true);
        var healths = SystemAPI.GetComponentLookup<Health>(true);
        var healing = SystemAPI.GetBufferLookup<HealingInstance>();

        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (heal, transform, source, entity) in SystemAPI.Query<RefRW<HealOnArrival>, RefRO<LocalTransform>, RefRO<Source>>()
                     .WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            var target = heal.ValueRO.Target;
            if (!transforms.HasComponent(target) || !healths.HasComponent(target) || !healing.HasBuffer(target))
            {
                commandBuffer.AddComponent(entity, new DestroyWithChildren());
                continue;
            }
            if (heal.ValueRO.ArmedIn > 0f)
            {
                heal.ValueRW.ArmedIn -= deltaTime;
                continue;
            }
            if (math.distance(transforms[target].Position.xz, transform.ValueRO.Position.xz) > ArriveDistance) continue;

            healing[target].Add(new HealingInstance { Value = healths[target].MaxValue * heal.ValueRO.HealShare + heal.ValueRO.Amount, Source = source.ValueRO.Entity, Label = heal.ValueRO.Label });
            commandBuffer.AddComponent(entity, new DestroyWithChildren());
        }
    }
}
