using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Tamed Winds unique power, added by TamedWindsPower while the item is equipped.</summary>
public struct TamedWinds : IComponentData
{
    public float CircleRadius;  // Meters from the aim point that the tornadoes circle at
    public float PullStrength;  // How hard a tornado is steered back to that circle, per meter off it
    public float TurnRate;      // How fast a tornado turns toward where it's steered (per second)
}

/// <summary>
/// Tamed Winds: the wearer's tornadoes stop wandering and head for the wearer's aim point (InputsData.MousePosition, the
/// mouse or the aim stick), then circle it at CircleRadius, so several form a ring around it. Runs between
/// WanderingProjectileSystem (whose sway it keeps) and ProjectileSystem.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(WanderingProjectileSystem))]
[UpdateBefore(typeof(ProjectileSystem))]
public partial struct TamedWindsSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<TamedWinds>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (projectile, wandering, attack, source, transform) in
                 SystemAPI.Query<RefRW<Projectile>, RefRW<WanderingProjectile>, RefRO<PeriodicAttack>, RefRO<Source>, RefRO<LocalTransform>>())
        {
            var owner = source.ValueRO.Entity;
            if (attack.ValueRO.Skill != SkillType.Tornado || !SystemAPI.HasComponent<TamedWinds>(owner) || !SystemAPI.HasComponent<InputsData>(owner)) continue;
            var winds = SystemAPI.GetComponent<TamedWinds>(owner);

            // No random turns
            wandering.ValueRW.Elapsed = 0f;

            // Toward the circle around the aim point, then along it
            var toCenter = SystemAPI.GetComponent<InputsData>(owner).MousePosition - transform.ValueRO.Position;
            toCenter.y = 0f;
            float distance = math.length(toCenter);
            if (distance < 1e-3f) continue;
            var inward = toCenter / distance;
            var along = new float3(inward.z, 0f, -inward.x);
            var desired = math.normalizesafe(along + inward * (distance - winds.CircleRadius) * winds.PullStrength, inward);

            var direction = projectile.ValueRO.Direction;
            projectile.ValueRW.Direction = math.normalizesafe(math.lerp(direction, desired, math.saturate(winds.TurnRate * deltaTime)), desired);
        }
    }
}
