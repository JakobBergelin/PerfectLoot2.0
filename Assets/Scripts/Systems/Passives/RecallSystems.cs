using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Recall: remembers when the wearer last used each skill slot itself (casts by effects don't count). Once every equipped
/// skill (at least two) has been used within Window seconds, it restores ResourceFraction of the resource and finishes
/// the shortest cooldown that's running, then starts over. A skill reaction, after this frame's skills reported their state.
/// </summary>
[UpdateInGroup(typeof(SkillReactionSystemGroup))]
[BurstCompile]
public partial struct RecallSystem : ISystem
{
    const double Never = -1e9;

    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Recall>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        double now = SystemAPI.Time.ElapsedTime;
        foreach (var (recall, skills, resource, resourceChanges) in
                 SystemAPI.Query<RefRW<Recall>, DynamicBuffer<Skill>, RefRO<Resource>, DynamicBuffer<ResourceChanger>>())
        {
            ref var r = ref recall.ValueRW;
            foreach (var skill in skills)
                if (skill.events.OnUsed && !skill.events.AutoCast && skill.SlotIndex >= 0 && skill.SlotIndex < 4)
                    r.LastUsed[skill.SlotIndex] = now;

            int equipped = 0;
            bool all = true;
            foreach (var skill in skills)
            {
                if (skill.SlotIndex < 0 || skill.SlotIndex >= 4) continue;
                equipped++;
                if (now - r.LastUsed[skill.SlotIndex] > r.Window) all = false;
            }
            if (equipped < 2 || !all) continue;
            r.LastUsed = new double4(Never);

            resourceChanges.Add(new ResourceChanger { Value = resource.ValueRO.MaxValue * r.ResourceFraction });

            int shortest = -1;
            for (int i = 0; i < skills.Length; i++)
            {
                float remaining = skills[i].State.CooldownRemaining;
                if (remaining > 0f && (shortest < 0 || remaining < skills[shortest].State.CooldownRemaining)) shortest = i;
            }
            if (shortest >= 0) skills.ElementAt(shortest).CooldownReduction += skills[shortest].State.CooldownRemaining;
        }
    }
}
