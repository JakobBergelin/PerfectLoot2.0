using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Gale Ward: its key is read here (never enabled, so a skill in a lower slot can't hold it back). Each press moves its
/// SkillState.Variant on: off, clockwise, counter-clockwise, then off again. While on and the caster can pay, it drains
/// ResourceCostPerSecond, sets the wind's Direction for GaleWardBendSystem and chills enemies in its band every
/// TickRate seconds. Without enough resource it stays on but pauses. It is active while on.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct GaleWardSystem : ISystem
{
    public const int VariantCount = 3;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GaleWard>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<StatusEffectConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var settings = SystemAPI.GetSingleton<StatusEffectConfig>();
        var chilledLookup = SystemAPI.GetComponentLookup<Chilled>();
        var modifiers = SystemAPI.GetComponentLookup<MovementModifiers>(true);
        state.CompleteDependency();  // The lookup is used here on the main thread; TimedEffectSystem ticks it in a job
        var hits = new NativeList<DistanceHit>(Allocator.Temp);
        var filter = new CollisionFilter { BelongsTo = ~0u, CollidesWith = (uint)CollisionLayerHelper.AnyEnemy, GroupIndex = 0 };

        foreach (var (ward, inputs, skills, resource, resourceChanges, stats, transform, entity) in
                 SystemAPI.Query<RefRW<GaleWard>, RefRO<InputsData>, DynamicBuffer<Skill>, RefRO<Resource>, DynamicBuffer<ResourceChanger>, RefRO<CalculatedStats>, RefRO<LocalTransform>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            ref var w = ref ward.ValueRW;
            w.Direction = 0f;
            int index = -1;
            if (!SkillUtil.FindSkill(skills, SkillType.GaleWard, ref index)) continue;
            ref var skill = ref skills.ElementAt(index);

            int slot = skill.SlotIndex;
            bool pressed = slot >= 0 && slot < 4 && inputs.ValueRO.SkillPressedThisFrame[slot];
            if (pressed && skill.State.Usable && !(modifiers.HasComponent(entity) && modifiers[entity].PreventsActions))
            {
                skill.State.Variant = (math.max(0, skill.State.Variant) + 1) % VariantCount;
                if (skill.State.Variant != 0) SkillUtil.MarkUsed(skills, SkillType.GaleWard);
            }
            if (!skill.State.Usable) skill.State.Variant = 0;
            skill.State.IsActive = skill.State.Variant != 0;
            if (skill.State.Variant == 0)
            {
                w.Elapsed = 0f;
                continue;
            }

            var statBlock = stats.ValueRO.Value;
            float drain = SkillUtil.Cost(w.ResourceCostPerSecond, statBlock) * deltaTime;
            if (resource.ValueRO.CurrentValue < drain) continue;
            resourceChanges.Add(new ResourceChanger { Value = -drain });
            w.Direction = skill.State.Variant == 1 ? 1f : -1f;

            w.Elapsed += deltaTime;
            if (w.Elapsed < w.TickRate) continue;
            w.Elapsed -= w.TickRate;

            float area = math.max(0f, statBlock.AreaMultiplier);
            float inner = w.InnerRadius * area;
            var position = transform.ValueRO.Position;
            physicsWorld.OverlapSphere(position, w.OuterRadius * area, ref hits, filter);
            foreach (var hit in hits)
            {
                if (!chilledLookup.HasComponent(hit.Entity) || !SystemAPI.HasComponent<LocalTransform>(hit.Entity)) continue;
                if (math.distancesq(SystemAPI.GetComponent<LocalTransform>(hit.Entity).Position.xz, position.xz) < inner * inner) continue;
                EffectApply.AddChill(chilledLookup, hit.Entity, w.ChillPerTick, settings.ChillMaxStacks, settings.ChillDuration);
            }
        }
    }
}

/// <summary>
/// The wind of every Gale Ward that's on: each projectile in a ward's band, anyone's, turns toward the wind's way around
/// the caster at TurnSpeed degrees per second, along the ground (a flying one keeps its climb or drop). After homing has
/// steered, so homing projectiles fight it with their own turn speed, and before projectiles move. Captain's Shield
/// steers itself and is left alone.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(HomingProjectileSystem))]
[UpdateBefore(typeof(ProjectileSystem))]
public partial struct GaleWardBendSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<GaleWard>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // The wards blowing: centre, inner and outer radius, and direction times turn speed (radians per second)
        var bands = new NativeList<float4>(Allocator.Temp);
        var centres = new NativeList<float2>(Allocator.Temp);
        foreach (var (ward, transform, stats) in SystemAPI.Query<RefRO<GaleWard>, RefRO<LocalTransform>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (ward.ValueRO.Direction == 0f) continue;
            float area = math.max(0f, stats.ValueRO.Value.AreaMultiplier);
            centres.Add(transform.ValueRO.Position.xz);
            bands.Add(new float4(ward.ValueRO.InnerRadius * area, ward.ValueRO.OuterRadius * area, ward.ValueRO.Direction * math.radians(ward.ValueRO.TurnSpeed), 0f));
        }
        if (bands.IsEmpty) return;

        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (projectile, transform) in SystemAPI.Query<RefRW<Projectile>, RefRW<LocalTransform>>().WithNone<ShieldProjectile, DestroyWithChildren>())
        {
            bool bent = false;
            for (int i = 0; i < bands.Length; i++)
            {
                float2 offset = transform.ValueRO.Position.xz - centres[i];
                float distanceSq = math.lengthsq(offset);
                if (distanceSq < bands[i].x * bands[i].x || distanceSq > bands[i].y * bands[i].y || distanceSq < 1e-6f) continue;

                // Clockwise seen from above (x right, z up the screen) is (z, -x); the sign of the turn speed picks the way
                float2 wind = math.normalize(new float2(offset.y, -offset.x)) * math.sign(bands[i].z);
                projectile.ValueRW.Direction = TurnToward(projectile.ValueRO.Direction, wind, math.abs(bands[i].z) * deltaTime);
                bent = true;
            }
            if (bent) transform.ValueRW.Rotation = quaternion.LookRotationSafe(projectile.ValueRO.Direction, math.up());
        }
    }

    // Turns direction's flat part toward wind by at most maxRadians, keeping its length and its vertical part
    static float3 TurnToward(float3 direction, float2 wind, float maxRadians)
    {
        float2 flat = direction.xz;
        float length = math.length(flat);
        if (length < 1e-6f) return direction;
        flat /= length;
        float angle = math.atan2(flat.x * wind.y - flat.y * wind.x, math.dot(flat, wind));
        float step = math.clamp(angle, -maxRadians, maxRadians);
        math.sincos(step, out float sin, out float cos);
        flat = new float2(flat.x * cos - flat.y * sin, flat.x * sin + flat.y * cos) * length;
        return new float3(flat.x, direction.y, flat.y);
    }
}
