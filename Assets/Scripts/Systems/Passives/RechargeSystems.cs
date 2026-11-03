using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Recharge: every ResourcePerTrigger resource spent on skills takes CooldownReduction seconds off one random equipped
/// skill tagged Cooldown that is on cooldown; with none on cooldown the cut is lost. Last of the skills, where the
/// negative ResourceChangers are this frame's skill costs: before Huskar turns them into damage (they still count) and
/// before Medusa adds what it absorbs (that doesn't). The skill's own system applies the cut next frame.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup), OrderLast = true)]
public partial struct RechargeSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (recharge, resourceChanges, skills) in SystemAPI.Query<RefRW<Recharge>, DynamicBuffer<ResourceChanger>, DynamicBuffer<Skill>>())
        {
            ref var r = ref recharge.ValueRW;
            foreach (var change in resourceChanges)
            {
                if (change.Value < 0f) r.Spent -= change.Value;
            }

            float perTrigger = math.max(r.ResourcePerTrigger, 1f);
            while (r.Spent >= perTrigger)
            {
                r.Spent -= perTrigger;

                // Picks evenly among the skills with time left after the cuts already waiting on them
                int picked = -1;
                int candidates = 0;
                for (int i = 0; i < skills.Length; i++)
                {
                    var skill = skills[i];
                    if ((skill.Tags & SkillTags.Cooldown) == 0 || skill.State.CooldownRemaining <= skill.CooldownReduction) continue;
                    candidates++;
                    if (r.Random.NextInt(candidates) == 0) picked = i;
                }
                if (picked >= 0) skills.ElementAt(picked).CooldownReduction += r.CooldownReduction;
            }
        }
    }
}
