using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SocialPlatforms;

[BurstCompile]
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

[BurstCompile]
public partial class BleedingUISystem : SystemBase
{
    public event System.Action<float, float3> OnBleeding;

    [BurstCompile]
    protected override void OnUpdate()
    {
        foreach (var (bleeding, localTransform, entity) in SystemAPI.Query<RefRW<Bleeding>, RefRO<LocalTransform>>().WithEntityAccess().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if(!SystemAPI.IsComponentEnabled<Bleeding>(entity) && bleeding.ValueRO.DamageToDisplay > 0.0f)
            {
                OnBleeding?.Invoke(bleeding.ValueRO.DamageToDisplay, localTransform.ValueRO.Position);
                bleeding.ValueRW.DamageToDisplay = 0.0f;
                bleeding.ValueRW.DisplayCounter = 0.0f;
                break;
            }
            if(SystemAPI.IsComponentEnabled<Bleeding>(entity))
            {
                bleeding.ValueRW.DisplayCounter += SystemAPI.Time.DeltaTime;
                if(bleeding.ValueRO.DisplayCounter > 1.0f)
                {
                    OnBleeding?.Invoke(bleeding.ValueRO.DamageToDisplay, localTransform.ValueRO.Position);
                    bleeding.ValueRW.DamageToDisplay = 0.0f;
                    bleeding.ValueRW.DisplayCounter = 0.0f;
                }

            }
            
        }

    }
}

[BurstCompile]
public partial class BurningUISystem : SystemBase
{
    public event System.Action<float, float3> OnBurning;

    [BurstCompile]
    protected override void OnUpdate()
    {
        foreach (var (burning, localTransform, entity) in SystemAPI.Query<RefRW<Burning>, RefRO<LocalTransform>>().WithEntityAccess().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if(!SystemAPI.IsComponentEnabled<Burning>(entity) && burning.ValueRO.DamageToDisplay > 0.0f)
            {
                OnBurning?.Invoke(burning.ValueRO.DamageToDisplay, localTransform.ValueRO.Position);
                burning.ValueRW.DamageToDisplay = 0.0f;
                burning.ValueRW.DisplayCounter = 0.0f;
                break;
            }
            if(SystemAPI.IsComponentEnabled<Burning>(entity))
            {
                burning.ValueRW.DisplayCounter += SystemAPI.Time.DeltaTime;
                if(burning.ValueRO.DisplayCounter > 1.0f)
                {
                    OnBurning?.Invoke(burning.ValueRO.DamageToDisplay, localTransform.ValueRO.Position);
                    burning.ValueRW.DamageToDisplay = 0.0f;
                    burning.ValueRW.DisplayCounter = 0.0f;
                }

            }
            
        }

    }
}

[BurstCompile]
public partial class PoisonedUISystem : SystemBase
{
    public event System.Action<float, float3> OnPoisoned;

    protected override void OnCreate() => RequireForUpdate<StatusEffectConfig>();

    [BurstCompile]
    protected override void OnUpdate()
    {
        var statusEffects = SystemAPI.GetSingleton<StatusEffectConfig>();
        foreach (var (poisoned, localTransform, entity) in SystemAPI.Query<RefRW<Poisioned>, RefRO<LocalTransform>>().WithEntityAccess().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if(!SystemAPI.IsComponentEnabled<Poisioned>(entity) && poisoned.ValueRO.DisplayCounter > 0.0f)
            {
                OnPoisoned?.Invoke(poisoned.ValueRO.DisplayCounter * statusEffects.PoisonDamagePerSecond, localTransform.ValueRO.Position);
                poisoned.ValueRW.DisplayCounter = 0.0f;
                break;
            }
            if(SystemAPI.IsComponentEnabled<Poisioned>(entity))
            {
                poisoned.ValueRW.DisplayCounter += SystemAPI.Time.DeltaTime;
                if(poisoned.ValueRO.DisplayCounter > 1.0f)
                {
                    OnPoisoned?.Invoke(poisoned.ValueRO.DisplayCounter * statusEffects.PoisonDamagePerSecond, localTransform.ValueRO.Position);
                    poisoned.ValueRW.DisplayCounter = 0.0f;
                }

            }
            
        }

    }
}

