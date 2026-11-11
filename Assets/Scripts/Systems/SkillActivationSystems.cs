using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Enables the skill component of one held slot: the highest Priority, and between equal ones the latest pressed (same
/// frame, no command buffer: enabling isn't a structural change), and fills in the input/resource parts of each Skill's SkillState. In a dungeon a skill whose
/// requirements (Skill.Requirements) the unit's attributes don't meet isn't usable: it can't be pressed, effects can't cast
/// it, and toggles stay off (their systems check SkillState.Usable). The Hub and other areas ignore requirements.
/// Skill components themselves are added/removed by ActiveSkillManager.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(InputSystemGroup))]
[UpdateBefore(typeof(SkillSystemGroup))]
public partial class SkillActivationSystem : SystemBase
{
    protected override void OnUpdate()
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        bool enforceRequirements = SystemAPI.TryGetSingleton<LevelArea>(out var area) && area.Kind == AreaKind.Dungeon;

        foreach (var (inputs, skills, entity) in SystemAPI.Query<RefRO<InputsData>, DynamicBuffer<Skill>>().WithEntityAccess())
        {
            // Only one skill at a time: of the held ones, the highest Priority; between equal ones, the latest pressed
            int activeSlot = int.MaxValue;
            int bestPriority = int.MinValue;
            double bestPressed = double.MinValue;
            // Frozen and similar: no skill can be used (all of them get disabled below)
            bool preventsActions = SystemAPI.HasComponent<MovementModifiers>(entity) && SystemAPI.GetComponent<MovementModifiers>(entity).PreventsActions;
            // Ball Lightning in flight: no skills either, nor casts requested by effects
            if (SystemAPI.HasComponent<BallLightning>(entity) && SystemAPI.GetComponent<BallLightning>(entity).Flying)
            {
                preventsActions = true;
                if (SystemAPI.HasBuffer<SkillCastRequest>(entity)) SystemAPI.GetBuffer<SkillCastRequest>(entity).Clear();
            }
            // Sonic Ball rolling: only Sonic Ball itself stays usable (held keeps it rolling)
            var calculatedStats = SystemAPI.HasComponent<CalculatedStats>(entity) ? SystemAPI.GetComponent<CalculatedStats>(entity) : default;
            var stats = calculatedStats.Value;
            UpdateUsable(entity, skills, ActiveSkills.SkillRequirements.Attributes(stats), enforceRequirements);

            bool rolling = SystemAPI.HasComponent<SonicBall>(entity) && SystemAPI.GetComponent<SonicBall>(entity).Rolling;
            // Hookshot Slash out or pulling: no skills (its own system carries on without being held)
            if (SystemAPI.HasComponent<HookshotSlash>(entity) && SystemAPI.GetComponent<HookshotSlash>(entity).State != HookshotState.Idle)
                preventsActions = true;
            double now = SystemAPI.Time.ElapsedTime;
            for (int i = 0; i < skills.Length; i++)
            {
                ref var skill = ref skills.ElementAt(i);
                if (IsPressed(inputs.ValueRO, skill.SlotIndex)) skill.LastPressed = now;
                if (preventsActions || !IsHeld(inputs.ValueRO, skill.SlotIndex) || !skill.State.Usable || (rolling && skill.Type != SkillType.SonicBall)) continue;
                bool better = skill.Priority > bestPriority
                              || (skill.Priority == bestPriority && skill.LastPressed > bestPressed)
                              || (skill.Priority == bestPriority && skill.LastPressed == bestPressed && skill.SlotIndex < activeSlot);
                if (!better) continue;
                activeSlot = skill.SlotIndex;
                bestPriority = skill.Priority;
                bestPressed = skill.LastPressed;
            }

            float resource = SystemAPI.HasComponent<Resource>(entity) ? SystemAPI.GetComponent<Resource>(entity).CurrentValue : float.MaxValue;

            for (int i = 0; i < skills.Length; i++)
            {
                ref var skill = ref skills.ElementAt(i);
                bool enabled = skill.SlotIndex == activeSlot;
                SetSkillEnabled(entity, skill.Type, enabled);

                skill.events.OnUsed = false;
                skill.events.AutoCast = false;
                skill.events.UsedAs = SkillTags.None;
                // Effects that change tags add theirs after this, every frame
                skill.Tags = skill.BaseTags;
                skill.State.IsPressed = IsHeld(inputs.ValueRO, skill.SlotIndex);
                skill.State.IsActive = enabled;  // Skills with a longer-running effect (Dash, Teleport, Whirlwind) override this
                skill.State.HasEnoughResource = SkillUtil.CanAfford(resource, SkillUtil.Cost(skill.State.ResourceCost, stats, skill.Tags), calculatedStats)
                                             && resource >= SkillUtil.Cost(skill.State.ResourceCostPerSecond, stats, skill.Tags) * deltaTime;
            }
        }
    }

    // Whether each skill's requirements are met and it can be used; casts effects requested for one that can't are dropped
    void UpdateUsable(Entity entity, DynamicBuffer<Skill> skills, float3 attributes, bool enforceRequirements)
    {
        for (int i = 0; i < skills.Length; i++)
        {
            ref var skill = ref skills.ElementAt(i);
            skill.State.MeetsRequirements = math.all(attributes >= skill.Requirements);
            skill.State.Usable = skill.State.MeetsRequirements || !enforceRequirements;
        }
        if (!SystemAPI.HasBuffer<SkillCastRequest>(entity)) return;
        var requests = SystemAPI.GetBuffer<SkillCastRequest>(entity);
        for (int r = requests.Length - 1; r >= 0; r--)
            foreach (var skill in skills)
                if (skill.Type == requests[r].Type && !skill.State.Usable)
                {
                    requests.RemoveAt(r);
                    break;
                }
    }

    static bool IsHeld(in InputsData inputs, int slotIndex) => slotIndex >= 0 && slotIndex < 4 && inputs.SkillHeld[slotIndex];

    static bool IsPressed(in InputsData inputs, int slotIndex) => slotIndex >= 0 && slotIndex < 4 && inputs.SkillPressedThisFrame[slotIndex];

    void SetSkillEnabled(Entity entity, SkillType type, bool enabled)
    {
        switch (type)
        {
            case SkillType.FistOfThunder:
                if (SystemAPI.HasComponent<FistsOfThunder>(entity)) SystemAPI.SetComponentEnabled<FistsOfThunder>(entity, enabled);
                break;
            case SkillType.Fireball:
                if (SystemAPI.HasComponent<FireBall>(entity)) SystemAPI.SetComponentEnabled<FireBall>(entity, enabled);
                break;
            case SkillType.Dash:
                if (SystemAPI.HasComponent<Dash>(entity)) SystemAPI.SetComponentEnabled<Dash>(entity, enabled);
                break;
            case SkillType.Whirlwind:
                if (SystemAPI.HasComponent<Whirlwind>(entity)) SystemAPI.SetComponentEnabled<Whirlwind>(entity, enabled);
                break;
            case SkillType.Tornado:
                if (SystemAPI.HasComponent<Tornado>(entity)) SystemAPI.SetComponentEnabled<Tornado>(entity, enabled);
                break;
            case SkillType.Teleport:
                if (SystemAPI.HasComponent<Teleport>(entity)) SystemAPI.SetComponentEnabled<Teleport>(entity, enabled);
                break;
            case SkillType.Invulnerability:
                if (SystemAPI.HasComponent<Invulnerability>(entity)) SystemAPI.SetComponentEnabled<Invulnerability>(entity, enabled);
                break;
            case SkillType.CaptainsShield:
                if (SystemAPI.HasComponent<CaptainsShield>(entity)) SystemAPI.SetComponentEnabled<CaptainsShield>(entity, enabled);
                break;
            case SkillType.BallLightning:
                if (SystemAPI.HasComponent<BallLightning>(entity)) SystemAPI.SetComponentEnabled<BallLightning>(entity, enabled);
                break;
            case SkillType.Flamethrower:
                if (SystemAPI.HasComponent<Flamethrower>(entity)) SystemAPI.SetComponentEnabled<Flamethrower>(entity, enabled);
                break;
            case SkillType.SonicBall:
                if (SystemAPI.HasComponent<SonicBall>(entity)) SystemAPI.SetComponentEnabled<SonicBall>(entity, enabled);
                break;
            case SkillType.HookshotSlash:
                if (SystemAPI.HasComponent<HookshotSlash>(entity)) SystemAPI.SetComponentEnabled<HookshotSlash>(entity, enabled);
                break;
            case SkillType.Shadowstep:
                if (SystemAPI.HasComponent<Shadowstep>(entity)) SystemAPI.SetComponentEnabled<Shadowstep>(entity, enabled);
                break;
            case SkillType.FrozenOrb:
                if (SystemAPI.HasComponent<FrozenOrb>(entity)) SystemAPI.SetComponentEnabled<FrozenOrb>(entity, enabled);
                break;
            case SkillType.PlagueArrow:
                if (SystemAPI.HasComponent<PlagueArrow>(entity)) SystemAPI.SetComponentEnabled<PlagueArrow>(entity, enabled);
                break;
            case SkillType.Swipe:
                if (SystemAPI.HasComponent<Swipe>(entity)) SystemAPI.SetComponentEnabled<Swipe>(entity, enabled);
                break;
            case SkillType.Upheaval:
                if (SystemAPI.HasComponent<Upheaval>(entity)) SystemAPI.SetComponentEnabled<Upheaval>(entity, enabled);
                break;
            case SkillType.PhantomEdge:
                if (SystemAPI.HasComponent<PhantomEdge>(entity)) SystemAPI.SetComponentEnabled<PhantomEdge>(entity, enabled);
                break;
            case SkillType.Meteor:
                if (SystemAPI.HasComponent<Meteor>(entity)) SystemAPI.SetComponentEnabled<Meteor>(entity, enabled);
                break;
            case SkillType.CursedHammer:
                if (SystemAPI.HasComponent<CursedHammer>(entity)) SystemAPI.SetComponentEnabled<CursedHammer>(entity, enabled);
                break;
            case SkillType.FrostLance:
                if (SystemAPI.HasComponent<FrostLance>(entity)) SystemAPI.SetComponentEnabled<FrostLance>(entity, enabled);
                break;
            case SkillType.BloodRite:
                if (SystemAPI.HasComponent<BloodRite>(entity)) SystemAPI.SetComponentEnabled<BloodRite>(entity, enabled);
                break;
            case SkillType.ShieldWall:
                if (SystemAPI.HasComponent<ShieldWall>(entity)) SystemAPI.SetComponentEnabled<ShieldWall>(entity, enabled);
                break;
            case SkillType.Riposte:
                if (SystemAPI.HasComponent<Riposte>(entity)) SystemAPI.SetComponentEnabled<Riposte>(entity, enabled);
                break;
            case SkillType.EarthBending:
                if (SystemAPI.HasComponent<EarthBending>(entity)) SystemAPI.SetComponentEnabled<EarthBending>(entity, enabled);
                break;
            case SkillType.Multishot:
                if (SystemAPI.HasComponent<Multishot>(entity)) SystemAPI.SetComponentEnabled<Multishot>(entity, enabled);
                break;
            case SkillType.ColdSnap:
                if (SystemAPI.HasComponent<ColdSnap>(entity)) SystemAPI.SetComponentEnabled<ColdSnap>(entity, enabled);
                break;
            case SkillType.Timezone:
                if (SystemAPI.HasComponent<Timezone>(entity)) SystemAPI.SetComponentEnabled<Timezone>(entity, enabled);
                break;
            case SkillType.TeslaCoil:
                if (SystemAPI.HasComponent<TeslaCoil>(entity)) SystemAPI.SetComponentEnabled<TeslaCoil>(entity, enabled);
                break;
            case SkillType.PoisonDart:
                if (SystemAPI.HasComponent<PoisonDart>(entity)) SystemAPI.SetComponentEnabled<PoisonDart>(entity, enabled);
                break;
            case SkillType.FireHydra:
                if (SystemAPI.HasComponent<FireHydra>(entity)) SystemAPI.SetComponentEnabled<FireHydra>(entity, enabled);
                break;
            case SkillType.MirrorImage:
                if (SystemAPI.HasComponent<MirrorImage>(entity)) SystemAPI.SetComponentEnabled<MirrorImage>(entity, enabled);
                break;
            case SkillType.StoneGuardian:
            case SkillType.Wolf:
            case SkillType.Wisp:
                SetSummonHeld(entity, type, enabled);
                break;
        }
    }

    void SetSummonHeld(Entity entity, SkillType type, bool held)
    {
        if (!SystemAPI.HasBuffer<CompanionSummon>(entity)) return;
        var summons = SystemAPI.GetBuffer<CompanionSummon>(entity);
        int index = CompanionSummon.Find(summons, type);
        if (index >= 0) summons.ElementAt(index).Held = held;
    }
}
