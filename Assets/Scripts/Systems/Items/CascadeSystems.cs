using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Cascade: a wearer whose Generator skill landed a critical hit this frame has Chance to ask for a cast of one of its
/// equipped Area skills, at random, toward where it aims. One roll per wearer and frame, however many hits crit. The
/// skill's own system casts it next frame (SkillCastRequest): free, no attack recovery, its cooldown untouched, counted
/// as an AutoCast use. Between working out the damage and dealing it, while the crits are still in CalculatedDamageInstance.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct CascadeSystem : ISystem
{
    Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Random.CreateFromIndex((uint)System.Environment.TickCount);
        state.RequireForUpdate<Cascade>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var wearers = SystemAPI.GetComponentLookup<Cascade>(true);
        var skills = SystemAPI.GetBufferLookup<Skill>(true);
        var critted = new NativeHashSet<Entity>(4, Allocator.Temp);

        foreach (var taken in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>())
        {
            foreach (var instance in taken)
            {
                if (!instance.IsCrit || instance.Hierarchy != DamageHierarchy.Main || instance.Damage <= 0f) continue;
                if (!wearers.HasComponent(instance.Source) || critted.Contains(instance.Source) || !skills.HasBuffer(instance.Source)) continue;
                foreach (var skill in skills[instance.Source])
                    if (skill.Label == instance.Origin.Label && skill.Label != DamageLabel.None && (skill.Tags & SkillTags.Generator) != 0)
                        critted.Add(instance.Source);
            }
        }
        if (critted.IsEmpty) return;

        foreach (var (cascade, desiredDirection, equipped, requests, entity) in
                 SystemAPI.Query<RefRO<Cascade>, RefRO<DesiredDirection>, DynamicBuffer<Skill>, DynamicBuffer<SkillCastRequest>>().WithEntityAccess())
        {
            if (!critted.Contains(entity) || _random.NextFloat() >= cascade.ValueRO.Chance) continue;

            var candidates = new FixedList64Bytes<SkillType>();
            foreach (var skill in equipped)
                if ((skill.Tags & SkillTags.Area) != 0 && candidates.Length < candidates.Capacity) candidates.Add(skill.Type);
            if (candidates.Length == 0) continue;

            requests.Add(new SkillCastRequest
            {
                Type = candidates[_random.NextInt(candidates.Length)],
                Direction = desiredDirection.ValueRO.Direction,
                AutoCast = true,
                Free = true,
            });
        }
    }
}
