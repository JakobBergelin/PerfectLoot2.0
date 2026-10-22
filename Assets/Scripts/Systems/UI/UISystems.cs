using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SocialPlatforms;

[BurstCompile]
[UpdateInGroup(typeof(FeedbackSystemGroup))]
partial struct ProjectileUISystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (projectile, localTransform) in SystemAPI.Query<RefRO<Projectile>, RefRO<LocalTransform>>())
        {
            ProgrammerArt.DrawSphere(localTransform.ValueRO.Position, localTransform.ValueRO.Scale / 2, Color.azure);
        }
    }
}

[BurstCompile]
[UpdateInGroup(typeof(FeedbackSystemGroup))]
partial struct AttackUISystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (attack, localTransform) in SystemAPI.Query<RefRO<Attack>, RefRO<LocalTransform>>())
        {
            ProgrammerArt.DrawSphere(localTransform.ValueRO.Position, attack.ValueRO.Radius, Color.darkRed);
        }

        foreach (var (periodicAttack, localTransform) in SystemAPI.Query<RefRO<PeriodicAttack>, RefRO<LocalTransform>>())
        {
            ProgrammerArt.DrawSphere(localTransform.ValueRO.Position, periodicAttack.ValueRO.Radius, Color.indianRed);
        }
    }
}


/// <summary>
/// Combat text for damage over time: shows the bleed, burn and poison damage a unit took, summed, about once a second
/// and once more when the last of it ends (EffectDamageSystem adds it up).
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
public partial class EffectDamageTextSystem : SystemBase
{
    const float Interval = 1.0f;

    public event System.Action<float, float3> OnBleeding;
    public event System.Action<float, float3> OnBurning;
    public event System.Action<float, float3> OnPoisoned;

    protected override void OnUpdate()
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (text, localTransform, entity) in SystemAPI.Query<RefRW<EffectDamageText>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            ref var t = ref text.ValueRW;
            if (t.Bleed <= 0.0f && t.Burn <= 0.0f && t.Poison <= 0.0f)
            {
                t.Timer = 0.0f;
                continue;
            }

            t.Timer += deltaTime;
            bool stillTicking = IsActive<Bleeding>(entity) || IsActive<Burning>(entity) || IsActive<Poisoned>(entity);
            if (t.Timer < Interval && stillTicking) continue;

            var position = localTransform.ValueRO.Position;
            if (t.Bleed > 0.0f) OnBleeding?.Invoke(t.Bleed, position);
            if (t.Burn > 0.0f) OnBurning?.Invoke(t.Burn, position);
            if (t.Poison > 0.0f) OnPoisoned?.Invoke(t.Poison, position);
            t = default;
        }
    }

    bool IsActive<T>(Entity entity) where T : unmanaged, IComponentData, IEnableableComponent
        => EntityManager.HasComponent<T>(entity) && EntityManager.IsComponentEnabled<T>(entity);
}
