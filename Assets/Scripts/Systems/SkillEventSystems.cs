using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Turns this frame's skill uses into events by tag: a used Melee skill enables MeleeAttackUsed (with its element), a used Mobility skill
/// MobilitySkillUsed (only the tags its uses were marked as, see SkillUtil.MarkUsed). First in SkillReactionSystemGroup,
/// so every reaction sees them.
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
            var meleeType = DamageType.Physical;
            foreach (var skill in skills)
            {
                if (!skill.events.OnUsed) continue;
                var tags = skill.Tags & skill.events.UsedAs;
                if ((tags & SkillTags.Melee) != 0 && (used & SkillTags.Melee) == 0) meleeType = ElementOf(skill.Tags);
                used |= tags;
            }

            if ((used & SkillTags.Melee) != 0 && melee.HasComponent(entity))
            {
                melee[entity] = new MeleeAttackUsed { Type = meleeType };
                melee.SetComponentEnabled(entity, true);
            }
            if ((used & SkillTags.Mobility) != 0 && mobility.HasComponent(entity)) mobility.SetComponentEnabled(entity, true);
        }
    }

    /// <summary>A skill's element from its element tags (the asset's own element, before conversion); Physical without one.</summary>
    static DamageType ElementOf(SkillTags tags) =>
        (tags & SkillTags.Fire) != 0 ? DamageType.Fire :
        (tags & SkillTags.Cold) != 0 ? DamageType.Cold :
        (tags & SkillTags.Lightning) != 0 ? DamageType.Lightning :
        (tags & SkillTags.Poison) != 0 ? DamageType.Poison :
        DamageType.Physical;
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
