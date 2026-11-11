using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

// Skills and passives that change Strength, Agility and Intelligence while playing, so skills' requirements can be met
// (or lost) in battle: Ambidextrous, Momentum of Mind, Stances and Blood Rite. Their attributes are added with the other
// flat stats, so everything that reads attributes (requirements, Shadow Priest, Balance) sees them.

/// <summary>Ambidextrous: its share of Agility also counts toward Strength requirements (SkillRequirements.Attributes).</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct AmbidextrousStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (ambidextrous, stats) in SystemAPI.Query<RefRO<Ambidextrous>, RefRW<CalculatedStats>>())
            stats.ValueRW.Value.AgilityForStrength += ambidextrous.ValueRO.AgilityForStrength;
    }
}

/// <summary>Momentum of Mind: Intelligence for each of the unit's skills on cooldown (as of last frame's skill states).</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct MomentumOfMindStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (momentum, skills, stats) in SystemAPI.Query<RefRO<MomentumOfMind>, DynamicBuffer<Skill>, RefRW<CalculatedStats>>())
        {
            int onCooldown = 0;
            foreach (var skill in skills)
                if (skill.State.CooldownRemaining > 0f) onCooldown++;
            stats.ValueRW.Value.Intelligence += onCooldown * momentum.ValueRO.IntelligencePerCooldown;
        }
    }
}

/// <summary>
/// Stances: its key is read here (never enabled, so a skill in a lower slot can't hold it back). Each press, Cooldown
/// apart, moves to the next stance in the skill's SkillState.Variant: Strength, Agility, Intelligence, then Strength again.
/// Reports the cooldown, and is active while it gives its bonus.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[BurstCompile]
public partial struct StancesSystem : ISystem
{
    public const int StanceCount = 3;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (stances, inputs, modifiers, skills) in SystemAPI.Query<RefRW<Stances>, RefRO<InputsData>, RefRO<MovementModifiers>, DynamicBuffer<Skill>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (!SkillUtil.FindSkill(skills, SkillType.Stances, ref index)) continue;
            ref var skill = ref skills.ElementAt(index);
            ref var s = ref stances.ValueRW;
            s.Elapsed = math.min(s.Elapsed + deltaTime, s.Cooldown);

            int slot = skill.SlotIndex;
            bool pressed = slot >= 0 && slot < 4 && inputs.ValueRO.SkillPressedThisFrame[slot];
            if (pressed && skill.State.Usable && !modifiers.ValueRO.PreventsActions && s.Elapsed >= s.Cooldown)
            {
                skill.State.Variant = (math.max(0, skill.State.Variant) + 1) % StanceCount;
                s.Elapsed = 0f;
                SkillUtil.MarkUsed(skills, SkillType.Stances);
            }

            skill.State.CooldownDuration = s.Cooldown;
            skill.State.CooldownRemaining = math.max(0f, s.Cooldown - s.Elapsed);
            skill.State.IsActive = skill.State.Usable;
        }
    }
}

/// <summary>Stances: the current stance's Bonus to its attribute, while the skill is usable.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct StancesStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (stances, skills, stats) in SystemAPI.Query<RefRO<Stances>, DynamicBuffer<Skill>, RefRW<CalculatedStats>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (!SkillUtil.FindSkill(skills, SkillType.Stances, ref index) || !skills[index].State.Usable) continue;
            float bonus = stances.ValueRO.Bonus;
            switch (skills[index].State.Variant)
            {
                case 1: stats.ValueRW.Value.Agility += bonus; break;
                case 2: stats.ValueRW.Value.Intelligence += bonus; break;
                default: stats.ValueRW.Value.Strength += bonus; break;
            }
        }
    }
}

/// <summary>
/// Blood Rite: when off cooldown, pays HealthCost of max health (never below 1) and grants the Blood Rite buff for
/// Duration, scaled by BuffDuration (restarting it). Reports the cooldown, and is active while the buff lasts.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[BurstCompile]
public partial struct BloodRiteSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var buffs = SystemAPI.GetComponentLookup<BloodRiteBuff>();
        state.CompleteDependency();  // The lookup is used here on the main thread; TimedEffectSystem ticks it in a job

        foreach (var (rite, stats) in SystemAPI.Query<RefRW<BloodRite>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            rite.ValueRW.Elapsed = math.min(rite.ValueRO.Elapsed + deltaTime, SkillUtil.Cooldown(rite.ValueRO.Cooldown, stats.ValueRO.Value));

        foreach (var (rite, health, skills, stats, entity) in
                 SystemAPI.Query<RefRW<BloodRite>, RefRW<Health>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            ref var r = ref rite.ValueRW;
            if (r.Elapsed < SkillUtil.Cooldown(r.Cooldown, stats.ValueRO.Value) || !buffs.HasComponent(entity)) continue;
            // Buff duration of -100% or less: nothing to grant, so no cast
            float duration = EffectApply.BuffDuration(r.Duration, stats.ValueRO.Value);
            if (duration <= 0f) continue;

            r.Elapsed = 0f;
            ref var h = ref health.ValueRW;
            h.CurrentValue = math.max(math.min(h.CurrentValue, 1f), h.CurrentValue - h.MaxValue * r.HealthCost);
            buffs[entity] = new BloodRiteBuff { Strength = r.StrengthBonus, Agility = r.AgilityBonus, TimeRemaining = duration, Duration = duration };
            buffs.SetComponentEnabled(entity, true);
            SkillUtil.MarkUsed(skills, SkillType.BloodRite);
        }

        foreach (var (rite, skills, stats, entity) in SystemAPI.Query<RefRW<BloodRite>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            int index = -1;
            if (!SkillUtil.FindSkill(skills, SkillType.BloodRite, ref index)) continue;
            ref var skill = ref skills.ElementAt(index);
            SkillUtil.ApplyCooldownReduction(ref skill, ref rite.ValueRW.Elapsed);
            float cooldown = SkillUtil.Cooldown(rite.ValueRO.Cooldown, stats.ValueRO.Value);
            skill.State.CooldownDuration = cooldown;
            skill.State.CooldownRemaining = math.max(0f, cooldown - rite.ValueRO.Elapsed);
            skill.State.IsActive = buffs.HasComponent(entity) && buffs.IsComponentEnabled(entity);
        }
    }
}

/// <summary>Adds the Blood Rite buff's Strength and Agility while it is active.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct BloodRiteBuffStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (buff, stats) in SystemAPI.Query<RefRO<BloodRiteBuff>, RefRW<CalculatedStats>>())
        {
            stats.ValueRW.Value.Strength += buff.ValueRO.Strength;
            stats.ValueRW.Value.Agility += buff.ValueRO.Agility;
        }
    }
}
