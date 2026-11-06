using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Bulwark: main hits on the wearer from in front of it deal FrontReduction less, hits from behind deal BackIncrease more.
/// Goes by where the attacker stands, or by the hit's direction once the attacker is gone: a melee hit's center can be
/// past the wearer. Between working out the damage and dealing it.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct BulwarkSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Bulwark>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var transforms = SystemAPI.GetComponentLookup<LocalTransform>(true);
        state.CompleteDependency();  // The lookup is used here on the main thread

        foreach (var (bulwark, taken, transform) in SystemAPI.Query<RefRO<Bulwark>, DynamicBuffer<CalculatedDamageInstance>, RefRO<LocalTransform>>())
        {
            var forward = transform.ValueRO.Forward();
            for (int i = 0; i < taken.Length; i++)
            {
                ref var instance = ref taken.ElementAt(i);
                if (instance.Hierarchy != DamageHierarchy.Main || instance.Damage <= 0f) continue;

                var toAttacker = transforms.HasComponent(instance.Source)
                    ? transforms[instance.Source].Position - transform.ValueRO.Position
                    : -instance.HitDirection;
                float facing = math.dot(toAttacker.xz, forward.xz);
                if (facing > 0f) instance.Damage *= 1f - bulwark.ValueRO.FrontReduction;
                else if (facing < 0f) instance.Damage *= 1f + bulwark.ValueRO.BackIncrease;
            }
        }
    }
}
