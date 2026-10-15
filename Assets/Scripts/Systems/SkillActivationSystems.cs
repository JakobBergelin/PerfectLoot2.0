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
            foreach (var skill in skills)
            {
                if (IsHeld(inputs.ValueRO, skill.SlotIndex) && skill.SlotIndex < activeSlot)
                    activeSlot = skill.SlotIndex;
            }

            float resource = SystemAPI.HasComponent<Resource>(entity) ? SystemAPI.GetComponent<Resource>(entity).CurrentValue : float.MaxValue;

            for (int i = 0; i < skills.Length; i++)
            {
                ref var skill = ref skills.ElementAt(i);
                bool enabled = skill.SlotIndex == activeSlot;
                SetSkillEnabled(entity, skill.Type, enabled);

                skill.events.OnUsed = false;
                skill.State.IsPressed = IsHeld(inputs.ValueRO, skill.SlotIndex);
                skill.State.IsActive = enabled;  // Skills with a longer-running effect (Dash, Whirlwind) override this
                skill.State.HasEnoughResource = resource >= skill.State.ResourceCost
                                             && resource >= skill.State.ResourceCostPerSecond * deltaTime;
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
        }
    }
}
