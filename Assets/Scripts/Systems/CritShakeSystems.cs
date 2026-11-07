using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Shakes the camera when a skill's hit crits, by that skill's Crit Shake (ActiveSkill.CritShake, in its caster's Skill
/// buffer). The strongest crit of the frame counts, not their sum, so an area crit on a crowd shakes like one crit.
/// Companions' hits count for their owner, their Source. Between working out the damage and dealing it, while the
/// crits are still in CalculatedDamageInstance.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct CritShakeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<CameraFollow>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var skills = SystemAPI.GetBufferLookup<Skill>(true);
        float shake = 0f;

        foreach (var taken in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>())
        {
            foreach (var instance in taken)
            {
                if (!instance.IsCrit || instance.Hierarchy != DamageHierarchy.Main || instance.Damage <= 0f) continue;
                if (!skills.HasBuffer(instance.Source)) continue;
                foreach (var skill in skills[instance.Source])
                    if (skill.Label == instance.Origin.Label && skill.Label != DamageLabel.None) shake = math.max(shake, skill.CritShake);
            }
        }

        if (shake <= 0f) return;
        foreach (var follow in SystemAPI.Query<RefRW<CameraFollow>>())
            follow.ValueRW.Trauma = math.max(follow.ValueRO.Trauma, shake);
    }
}
