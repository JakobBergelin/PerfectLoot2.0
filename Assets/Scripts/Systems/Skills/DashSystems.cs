using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct DashSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (dash, localTransform) in SystemAPI.Query<RefRW<Dash>, RefRW<LocalTransform>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            dash.ValueRW.Elapsed += SystemAPI.Time.DeltaTime;
            dash.ValueRW.Elapsed = Mathf.Min(dash.ValueRW.Elapsed, dash.ValueRO.Cooldown);

            if(dash.ValueRO.Dashing)
            {
                // TODO disable inputs while dashing?
                localTransform.ValueRW.Position = math.lerp(localTransform.ValueRW.Position, dash.ValueRO.NextPosition, Time.deltaTime * 25f);

                if(math.distance(localTransform.ValueRW.Position, dash.ValueRO.NextPosition) < 1.0f)
                {
                    dash.ValueRW.Dashing = false;
                }
            }
        }

        foreach (var (dash, localTransform, desiredDirection, skillBuffer, entity) in SystemAPI.Query<RefRW<Dash>, RefRW<LocalTransform>, RefRO<DesiredDirection>, DynamicBuffer<Skill>>().WithEntityAccess())
        {
            if(dash.ValueRO.Elapsed >= dash.ValueRO.Cooldown)
            {
                localTransform.ValueRW.Rotation = desiredDirection.ValueRO.Quaternion;
                dash.ValueRW.Elapsed = 0.0f;
                dash.ValueRW.Dashing = true;
                dash.ValueRW.NextPosition = localTransform.ValueRW.Position + desiredDirection.ValueRO.Direction * 7.0f;

                // A use (tagged Mobility, so it also raises MobilitySkillUsed for e.g. Refractionist)
                SkillUtil.MarkUsed(skillBuffer, SkillType.Dash);
            }

        }

        // Report cooldown and dashing state (after SkillActivationSystem's defaults)
        foreach (var (dash, skillBuffer) in SystemAPI.Query<RefRO<Dash>, DynamicBuffer<Skill>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.Dash, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                skill.State.CooldownDuration = dash.ValueRO.Cooldown;
                skill.State.CooldownRemaining = math.max(0f, dash.ValueRO.Cooldown - dash.ValueRO.Elapsed);
                skill.State.IsActive = dash.ValueRO.Dashing;
            }
        }
    }
}
