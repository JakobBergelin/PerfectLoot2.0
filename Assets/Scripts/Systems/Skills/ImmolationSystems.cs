using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Immolation: pressing its key switches it on or off, even while another skill is held, and its slot stays lit while
/// it's on. While on and the caster can pay ResourceCostPerSecond, every TickRate seconds each enemy within Radius takes
/// (DamagePerSecond + Intelligence * DamagePerIntelligence) * TickRate as a normal main fire hit: damage %, element
/// conversion, crits and burning apply. Without enough resource it stays on but neither drains nor burns.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[BurstCompile]
public partial struct ImmolationSystem : ISystem
{
    Unity.Mathematics.Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)System.Environment.TickCount);
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<Immolation>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        float deltaTime = SystemAPI.Time.DeltaTime;
        var resourceChanges = SystemAPI.GetBufferLookup<ResourceChanger>();
        var hits = new NativeList<DistanceHit>(Allocator.Temp);
        var filter = new CollisionFilter { BelongsTo = ~0u, CollidesWith = (uint)CollisionLayerHelper.AnyEnemy, GroupIndex = 0 };
        state.CompleteDependency();  // Lookups and the EntityManager (crits) are used here on the main thread

        // Never enabled: the key is read here, so that a skill in a lower slot can't hold it back
        foreach (var (immolation, inputs, modifiers, skills, resource, stats, transform, entity) in
                 SystemAPI.Query<RefRW<Immolation>, RefRO<InputsData>, RefRO<MovementModifiers>, DynamicBuffer<Skill>, RefRO<Resource>, RefRO<CalculatedStats>, RefRO<LocalTransform>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            int index = -1;
            if (!SkillUtil.FindSkill(skills, SkillType.Immolation, ref index)) continue;

            int slot = skills[index].SlotIndex;
            if (slot >= 0 && slot < 4 && inputs.ValueRO.SkillPressedThisFrame[slot] && !modifiers.ValueRO.PreventsActions)
            {
                immolation.ValueRW.Active = !immolation.ValueRO.Active;
                immolation.ValueRW.Elapsed = 0f;
                SkillUtil.MarkUsed(skills, SkillType.Immolation);
            }
            skills.ElementAt(index).State.IsActive = immolation.ValueRO.Active;

            var statBlock = stats.ValueRO.Value;
            float drain = SkillUtil.Cost(immolation.ValueRO.ResourceCostPerSecond, statBlock) * deltaTime;
            if (!immolation.ValueRO.Active || resource.ValueRO.CurrentValue < drain) continue;

            if (resourceChanges.HasBuffer(entity)) resourceChanges[entity].Add(new ResourceChanger { Value = -drain });
            immolation.ValueRW.Elapsed += deltaTime;
            var im = immolation.ValueRO;
            if (im.Elapsed < im.TickRate) continue;
            immolation.ValueRW.Elapsed -= im.TickRate;

            var type = statBlock.ConvertedType(DamageType.Fire);
            float baseDamage = (im.DamagePerSecond + statBlock.Intelligence * im.DamagePerIntelligence) * im.TickRate * statBlock.DamageMultiplier(type);
            var position = transform.ValueRO.Position;

            hits.Clear();
            physicsWorld.OverlapSphere(position, im.Radius * statBlock.AreaMultiplier, ref hits, filter);
            foreach (var hit in hits)
            {
                if (hit.Entity == entity || !SystemAPI.HasComponent<Enemy>(hit.Entity) || !SystemAPI.HasComponent<Health>(hit.Entity) || !SystemAPI.HasBuffer<DamageInstance>(hit.Entity)) continue;

                float damage = baseDamage;
                bool isCrit = false;
                AttackSystem.CalculateCrit(entity, hit.Entity, state.EntityManager, ref _random, ref damage, ref isCrit);
                var targetPosition = SystemAPI.GetComponent<LocalTransform>(hit.Entity).Position;
                SystemAPI.GetBuffer<DamageInstance>(hit.Entity).Add(new DamageInstance
                {
                    Damage = damage, Type = type, Hierarchy = DamageHierarchy.Main, Origin = DamageLabel.Immolation, Source = entity, IsCrit = isCrit,
                    HitDirection = AttackSystem.HitDirection(position, targetPosition, ref _random),
                });
            }
        }
    }
}

/// <summary>Placeholder art: a circle of Immolation's radius around each caster that has it on.</summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
[BurstCompile]
partial struct ImmolationUISystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (immolation, stats, transform) in SystemAPI.Query<RefRO<Immolation>, RefRO<CalculatedStats>, RefRO<LocalTransform>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!immolation.ValueRO.Active) continue;
            float radius = immolation.ValueRO.Radius * stats.ValueRO.Value.AreaMultiplier;
            var center = transform.ValueRO.Position;
            const int Segments = 32;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * math.PI2 / Segments, b = (i + 1) * math.PI2 / Segments;
                Debug.DrawLine(center + new float3(math.cos(a) * radius, 0.05f, math.sin(a) * radius),
                               center + new float3(math.cos(b) * radius, 0.05f, math.sin(b) * radius), Color.orangeRed);
            }
        }
    }
}
