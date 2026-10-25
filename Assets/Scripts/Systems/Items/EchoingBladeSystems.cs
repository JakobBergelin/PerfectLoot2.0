using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Echoing Blade: a wearer that made a melee attack this frame (MeleeAttackUsed) with its resource at least
/// ResourceThreshold full asks for a cast of one of its equipped skills tagged Projectile, at random, toward the cursor.
/// The skill's own system casts it next frame (SkillCastRequest): its normal cost, no wind-up, counted as an AutoCast use.
/// </summary>
[UpdateInGroup(typeof(SkillReactionSystemGroup))]
[BurstCompile]
public partial struct EchoingBladeSystem : ISystem
{
    Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Random.CreateFromIndex((uint)System.Environment.TickCount);
        state.RequireForUpdate<EchoingBlade>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (blade, resource, desiredDirection, skills, requests) in
                 SystemAPI.Query<RefRO<EchoingBlade>, RefRO<Resource>, RefRO<DesiredDirection>, DynamicBuffer<Skill>, DynamicBuffer<SkillCastRequest>>()
                          .WithAll<MeleeAttackUsed>())
        {
            if (resource.ValueRO.MaxValue <= 0f || resource.ValueRO.CurrentValue < resource.ValueRO.MaxValue * blade.ValueRO.ResourceThreshold) continue;

            var candidates = new FixedList64Bytes<SkillType>();
            foreach (var skill in skills)
                if ((skill.Tags & SkillTags.Projectile) != 0 && candidates.Length < candidates.Capacity) candidates.Add(skill.Type);
            if (candidates.Length == 0) continue;

            requests.Add(new SkillCastRequest
            {
                Type = candidates[_random.NextInt(candidates.Length)],
                Direction = desiredDirection.ValueRO.Direction,
                AutoCast = true,
            });
        }
    }
}
