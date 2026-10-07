using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct DashSystem : ISystem
{
    private const float Cooldown = 2.0f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var skillBuffer in SystemAPI.Query<DynamicBuffer<Skill>>().WithPresent<Dash>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.Dash, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);

                skill.UIInformation.MaxCooldown = 2.0f;
                skill.UIInformation.CurrentCooldown = 0.0f;
                skill.UIInformation.ResourceCost = 0.0f;
                skill.UIInformation.ResourceCostPerSecond = 0.0f;
            }
        }

        foreach (var (dash, localTransform) in SystemAPI.Query<RefRW<Dash>, RefRW<LocalTransform>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            dash.ValueRW.Elapsed += SystemAPI.Time.DeltaTime;
            dash.ValueRW.Elapsed = Mathf.Min(dash.ValueRW.Elapsed, Cooldown);

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

        foreach (var (dash, localTransform, desiredDirection, skillBuffer) in SystemAPI.Query<RefRW<Dash>, RefRW<LocalTransform>, RefRO<DesiredDirection>, DynamicBuffer<Skill>>())
        {
            if(dash.ValueRO.Elapsed >= Cooldown)
            {
                localTransform.ValueRW.Rotation = desiredDirection.ValueRO.Quaternion;
                dash.ValueRW.Elapsed = 0.0f;
                dash.ValueRW.Dashing = true;
                dash.ValueRW.NextPosition = localTransform.ValueRW.Position + desiredDirection.ValueRO.Direction * 7.0f;
            }
            
        }
    }
}
