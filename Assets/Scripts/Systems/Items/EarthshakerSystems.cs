using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>Earthshaker legendary power, added by EarthshakerPower while the item is equipped.</summary>
public struct Earthshaker : IComponentData
{
    public float Damage;                // Physical, before the wearer's damage %
    public float DamagePerStrength;     // Added to Damage for each point of the wearer's Strength
    public float Radius;                // Scaled by the wearer's area of effect
    public float HitForce;              // Knockback, away from the wearer
}

/// <summary>
/// Earthshaker: a wearer that used a skill tagged Cooldown this frame sends out a shockwave. Enemies within Radius take
/// Damage + Strength * DamagePerStrength as a normal main physical hit by the wearer: damage %, element conversion,
/// crits and bleeding apply. A reaction, after every skill has run, so it goes off where the skill left the wearer.
/// </summary>
[UpdateInGroup(typeof(SkillReactionSystemGroup))]
[BurstCompile]
public partial struct EarthshakerSystem : ISystem
{
    Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Random.CreateFromIndex((uint)System.Environment.TickCount);
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<Earthshaker>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var hits = new NativeList<DistanceHit>(Allocator.Temp);
        var filter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = (uint)CollisionLayerHelper.AnyEnemy,
            GroupIndex = 0,
        };

        foreach (var (earthshaker, skills, stats, transform, entity) in
                 SystemAPI.Query<RefRO<Earthshaker>, DynamicBuffer<Skill>, RefRO<CalculatedStats>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            bool used = false;
            foreach (var skill in skills)
                if (skill.events.OnUsed && (skill.Tags & SkillTags.Cooldown) != 0) used = true;
            if (!used) continue;

            var e = earthshaker.ValueRO;
            var statBlock = stats.ValueRO.Value;
            var position = transform.ValueRO.Position;
            var type = statBlock.ConvertedType(DamageType.Physical);
            float baseDamage = (e.Damage + statBlock.Strength * e.DamagePerStrength) * statBlock.DamageMultiplier(type);

            hits.Clear();
            physicsWorld.OverlapSphere(position, e.Radius * statBlock.AreaMultiplier, ref hits, filter);
            foreach (var hit in hits)
            {
                if (hit.Entity == entity || !SystemAPI.HasComponent<Health>(hit.Entity) || !SystemAPI.HasBuffer<DamageInstance>(hit.Entity)) continue;

                float damage = baseDamage;
                bool isCrit = false;
                AttackSystem.CalculateCrit(entity, hit.Entity, state.EntityManager, ref _random, ref damage, ref isCrit);
                var targetPosition = SystemAPI.GetComponent<LocalTransform>(hit.Entity).Position;
                SystemAPI.GetBuffer<DamageInstance>(hit.Entity).Add(new DamageInstance
                {
                    Damage = damage, Type = type, Hierarchy = DamageHierarchy.Main, Source = entity, IsCrit = isCrit,
                    HitDirection = AttackSystem.HitDirection(position, targetPosition, ref _random), HitForce = e.HitForce,
                });
            }
        }
    }
}
