using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Long Shot: the wearer's projectile hits deal more the further the projectile is from where it was fired. Measured flat
/// from that spot, so orbiting or homing adds nothing. Between working out the damage and dealing it.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct LongShotSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<LongShot>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var longShots = SystemAPI.GetComponentLookup<LongShot>(true);
        var projectiles = SystemAPI.GetComponentLookup<Projectile>(true);
        var transforms = SystemAPI.GetComponentLookup<LocalTransform>(true);

        foreach (var taken in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>())
        {
            for (int i = 0; i < taken.Length; i++)
            {
                ref var instance = ref taken.ElementAt(i);
                if (!longShots.HasComponent(instance.Source) || !projectiles.HasComponent(instance.Attack)) continue;

                var longShot = longShots[instance.Source];
                float distance = math.distance(transforms[instance.Attack].Position.xz, projectiles[instance.Attack].StartPosition.xz);
                instance.Damage *= 1f + math.min(longShot.MaxBonus, longShot.DamagePerMeter * distance);
            }
        }
    }
}
