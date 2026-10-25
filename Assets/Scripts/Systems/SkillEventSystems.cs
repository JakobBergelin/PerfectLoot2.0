using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Turns this frame's skill uses into events by tag: a used Melee skill enables MeleeAttackUsed, a used Mobility skill
/// MobilitySkillUsed. First in SkillReactionSystemGroup, so every reaction sees them.
/// </summary>
[UpdateInGroup(typeof(SkillReactionSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct SkillTagEventSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var melee = SystemAPI.GetComponentLookup<MeleeAttackUsed>();
        var mobility = SystemAPI.GetComponentLookup<MobilitySkillUsed>();

        foreach (var (skills, entity) in SystemAPI.Query<DynamicBuffer<Skill>>().WithEntityAccess())
        {
            var used = SkillTags.None;
            foreach (var skill in skills)
                if (skill.events.OnUsed) used |= skill.Tags;

            if ((used & SkillTags.Melee) != 0 && melee.HasComponent(entity)) melee.SetComponentEnabled(entity, true);
            if ((used & SkillTags.Mobility) != 0 && mobility.HasComponent(entity)) mobility.SetComponentEnabled(entity, true);
        }
    }
}

/// <summary>
/// Clears cast requests nobody handled this frame (e.g. the skill was unequipped), just before reactions make new ones.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup), OrderLast = true)]
[UpdateBefore(typeof(SkillReactionSystemGroup))]
[BurstCompile]
public partial struct SkillCastRequestCleanupSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var requests in SystemAPI.Query<DynamicBuffer<SkillCastRequest>>())
            requests.Clear();
    }
}
