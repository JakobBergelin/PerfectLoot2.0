using Unity.Entities;

/// <summary>
/// Enables the skill component of the pressed slot (same frame, no command buffer: enabling isn't a
/// structural change) and fills in the input/resource parts of each Skill's SkillState.
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

        foreach (var (inputs, skills, entity) in SystemAPI.Query<RefRO<InputsData>, DynamicBuffer<Skill>>().WithEntityAccess())
        {
            // Only one skill at a time: the lowest held slot wins
            // TODO, replace with prio order, dash pressed should have prio over fireball etc.
            int activeSlot = int.MaxValue;
            // Frozen and similar: no skill can be used (all of them get disabled below)
            bool preventsActions = SystemAPI.HasComponent<MovementModifiers>(entity) && SystemAPI.GetComponent<MovementModifiers>(entity).PreventsActions;
            // Ball Lightning in flight: no skills either, nor casts requested by effects
            if (SystemAPI.HasComponent<BallLightning>(entity) && SystemAPI.GetComponent<BallLightning>(entity).Flying)
            {
                preventsActions = true;
                if (SystemAPI.HasBuffer<SkillCastRequest>(entity)) SystemAPI.GetBuffer<SkillCastRequest>(entity).Clear();
            }
            // Sonic Ball rolling: only Sonic Ball itself stays usable (held keeps it rolling)
            bool rolling = SystemAPI.HasComponent<SonicBall>(entity) && SystemAPI.GetComponent<SonicBall>(entity).Rolling;
            // Hookshot Slash out or pulling: no skills (its own system carries on without being held)
            if (SystemAPI.HasComponent<HookshotSlash>(entity) && SystemAPI.GetComponent<HookshotSlash>(entity).State != HookshotState.Idle)
                preventsActions = true;
            foreach (var skill in skills)
            {
                if (!preventsActions && IsHeld(inputs.ValueRO, skill.SlotIndex) && skill.SlotIndex < activeSlot && (!rolling || skill.Type == SkillType.SonicBall))
                    activeSlot = skill.SlotIndex;
            }

            float resource = SystemAPI.HasComponent<Resource>(entity) ? SystemAPI.GetComponent<Resource>(entity).CurrentValue : float.MaxValue;
            var calculatedStats = SystemAPI.HasComponent<CalculatedStats>(entity) ? SystemAPI.GetComponent<CalculatedStats>(entity) : default;
            var stats = calculatedStats.Value;

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
                skill.State.HasEnoughResource = SkillUtil.CanAfford(resource, SkillUtil.Cost(skill.State.ResourceCost, stats), calculatedStats)
                                             && resource >= SkillUtil.Cost(skill.State.ResourceCostPerSecond, stats) * deltaTime;
            }
        }
    }

    static bool IsHeld(in InputsData inputs, int slotIndex) => slotIndex >= 0 && slotIndex < 4 && inputs.SkillHeld[slotIndex];

    void SetSkillEnabled(Entity entity, SkillType type, bool enabled)
    {
        switch (type)
        {
            case SkillType.FistOfThunder:
                if (SystemAPI.HasComponent<FistsOfThunder>(entity)) SystemAPI.SetComponentEnabled<FistsOfThunder>(entity, enabled);
                break;
            case SkillType.Barrage:
                if (SystemAPI.HasComponent<Barrage>(entity)) SystemAPI.SetComponentEnabled<Barrage>(entity, enabled);
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
        }
    }
}
