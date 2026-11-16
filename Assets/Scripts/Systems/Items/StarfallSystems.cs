using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Starfall unique power, added by StarfallPower while the item is equipped. MeteorSystem leaves its wearer alone.</summary>
public struct Starfall : IComponentData
{
    public float ResourceCostPerSecond;
    public float Interval;          // Seconds between meteors (the shared AttackRecovery, so attack speed scales it)
    public float Spread;            // Meters around the aim point the meteors land in
    public float Delay;             // Seconds from a meteor's call to its impact
    public float DamageShare;       // Of Meteor's damage, impact and burning ground
    public float RadiusShare;       // Of Meteor's radius, impact and burning ground
    public float HazardDuration;
    public float Slow;              // Movement speed lost while channeling
    public bool Channeling;         // Runtime: holding Meteor and paying for it
}

/// <summary>
/// Starfall: while the wearer holds Meteor and can pay the drain, they're Channeling, and every Interval (the shared
/// AttackRecovery) a small meteor is called down at a random spot within Spread of the aim point, clamped to Meteor's
/// Range. MeteorStrikeSystem does the rest. Casts requested by effects are ignored. After WhirlwindSystem: that system
/// takes Channeling off whenever it isn't spinning, so it's added back here every frame.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateAfter(typeof(WhirlwindSystem))]
public partial struct StarfallSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Starfall>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float deltaTime = SystemAPI.Time.DeltaTime;
        var random = Random.CreateFromIndex((uint)(SystemAPI.Time.ElapsedTime * 1000.0));

        var skills = SystemAPI.GetBufferLookup<Skill>();
        var resourceChanges = SystemAPI.GetBufferLookup<ResourceChanger>();
        foreach (var (starfall, meteor, transform, inputs, resource, stats, recovery, entity) in
                 SystemAPI.Query<RefRW<Starfall>, RefRO<Meteor>, RefRO<LocalTransform>, RefRO<InputsData>, RefRO<Resource>, RefRO<CalculatedStats>,
                     RefRW<AttackRecovery>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            ref var s = ref starfall.ValueRW;
            bool held = SystemAPI.IsComponentEnabled<Meteor>(entity);
            float drain = SkillUtil.ChannelCost(s.ResourceCostPerSecond, stats.ValueRO.Value) * deltaTime;
            bool channeling = held && resource.ValueRO.CurrentValue >= drain;

            if (channeling != s.Channeling)
            {
                s.Channeling = channeling;
                if (!channeling) commandBuffer.RemoveComponent<Channeling>(entity);
            }
            if (!channeling) continue;

            commandBuffer.AddComponent<Channeling>(entity);
            if (resourceChanges.HasBuffer(entity)) resourceChanges[entity].Add(new ResourceChanger { Value = -drain });
            if (recovery.ValueRO.Remaining > 0f) continue;

            // A random spot around the aim point, which is at most Range away (on the ground plane, at the caster's height like Meteor)
            var m = meteor.ValueRO;
            var origin = transform.ValueRO.Position;
            var target = inputs.ValueRO.MousePosition;
            var offset = new float2(target.x - origin.x, target.z - origin.z);
            float distance = math.length(offset);
            if (distance > m.Range) offset *= m.Range / distance;
            offset += random.NextFloat2Direction() * s.Spread * math.sqrt(random.NextFloat());
            var spot = new float3(origin.x + offset.x, origin.y, origin.z + offset.y);

            var strike = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(strike, LocalTransform.FromPosition(spot));
            commandBuffer.AddComponent(strike, new MeteorStrike
            {
                Delay = s.Delay, FallTime = s.Delay, Damage = m.Damage * s.DamageShare, Radius = m.Radius * s.RadiusShare,
                HazardDamagePerSecond = m.HazardDamagePerSecond * s.DamageShare, HazardRadius = m.HazardRadius * s.RadiusShare,
                HazardDuration = s.HazardDuration,
            });
            commandBuffer.AddComponent(strike, new Source { Entity = entity });

            recovery.ValueRW.Remaining += s.Interval;
            if (skills.HasBuffer(entity)) SkillUtil.MarkUsed(skills[entity], SkillType.Meteor);
        }
    }
}

/// <summary>Slows a unit while it channels Starfall (from the previous frame's state).</summary>
[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct StarfallSlowSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (starfall, stats) in SystemAPI.Query<RefRO<Starfall>, RefRW<CalculatedStats>>())
            if (starfall.ValueRO.Channeling) stats.ValueRW.Value.MovementSpeed -= starfall.ValueRO.Slow;
    }
}
