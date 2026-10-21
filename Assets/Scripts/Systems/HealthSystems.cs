using Unity.Burst;
using Unity.Entities;
using UnityEngine;
using Unity.Collections;
using Unity.Transforms;
using Unity.Mathematics;

// TODO: Replace the stick/pivot bars (here and in ResourceUISystem) with a single billboard quad per bar.
// The shader faces the camera and draws the fill from a per-entity [MaterialProperty] float, so the CPU only
// writes one value when health changes. Scales to many enemies and makes the ordering below unnecessary.

// Counter-rotates the bar against the unit's rotation, so it has to run after everything that rotates units
// (physics runs first in the frame) and before the transforms are calculated
[UpdateAfter(typeof(CharacterMovementSystem))]
[UpdateAfter(typeof(EnemyMovementSystem))]
[UpdateAfter(typeof(EnemyMeleeAttackSystem))]
[UpdateAfter(typeof(SkillSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
public partial class HealthUISystem : SystemBase
{
    protected override void OnUpdate()
    {
        Vector3 cameraForward = Vector3.zero;
        if(Camera.main != null)
        {
            cameraForward = Camera.main.transform.forward;
        }

        foreach (var (healthBar, health, localTransform) in SystemAPI.Query<RefRO<HealthBarUI>, RefRO<Health>, RefRO<LocalTransform>>())
        {
            float healthNormalized = health.ValueRO.CurrentValue / health.ValueRO.MaxValue;
            var stickTransform = SystemAPI.GetComponentRW<LocalTransform>(healthBar.ValueRO.StickEntity);
            var pivotMatrix = SystemAPI.GetComponentRW<PostTransformMatrix>(healthBar.ValueRO.PivotEntity);
            pivotMatrix.ValueRW.Value = float4x4.Scale(healthNormalized, 1, 1);

            stickTransform.ValueRW.Rotation = localTransform.ValueRO.InverseTransformRotation(quaternion.LookRotation(cameraForward, math.up()));
        }
    }
}

partial struct HealthRegenerationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (health, healthRegeneration) in SystemAPI.Query<RefRW<Health>, RefRO<HealthRegeneration>>().WithAbsent<Golem>())
        {
            health.ValueRW.CurrentValue += healthRegeneration.ValueRO.Value * SystemAPI.Time.DeltaTime;
            health.ValueRW.CurrentValue = Mathf.Min(health.ValueRO.CurrentValue, health.ValueRO.MaxValue);
        }
    }
}
partial struct CheckDeathSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var toDestroy = new NativeList<Entity>(Allocator.Temp);
        foreach (var (health, entity) in SystemAPI.Query<RefRO<Health>>().WithEntityAccess())
        {
            if(health.ValueRO.CurrentValue <= 0.0f)
            {
                toDestroy.Add(entity);
            }
        }

        if(!toDestroy.IsEmpty)
        {
            state.EntityManager.AddComponent<DestroyWithChildren>(toDestroy.AsArray());
        }
    }
}


// If all DestroyWithChildren tags are added to the EndSimulationEntityCommandBufferSystem, they should get destroyed the same frame if this runs after
[UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]
partial struct DestroyWithChildrenSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var toDestroy = new NativeList<Entity>(Allocator.Temp);
        var toDestroyNext = new NativeList<Entity>(Allocator.Temp);

        foreach (var (destroy, entity) in SystemAPI.Query<RefRO<DestroyWithChildren>>().WithEntityAccess())
        {
            toDestroy.Add(entity);
            if (state.EntityManager.HasBuffer<Child>(entity))
            {
                // TODO remove frame delay
                foreach(var child in state.EntityManager.GetBuffer<Child>(entity))
                {
                    toDestroyNext.Add(child.Value);
                }
            }
        }

        if(!toDestroy.IsEmpty)
        {
            state.EntityManager.DestroyEntity(toDestroy.AsArray());  
        }
        if(!toDestroyNext.IsEmpty)
        {
            state.EntityManager.AddComponent<DestroyWithChildren>(toDestroyNext.AsArray());
        }
    }
}

[UpdateAfter(typeof(AttackSystemGroup))]
[UpdateBefore(typeof(DamagingSystem))]
public partial class CalculateDamageSystem : SystemBase
{
    public event System.Action<float, float3> OnDamageBlocked;
    private Unity.Mathematics.Random _random;

    protected override void OnCreate()
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)System.Environment.TickCount);
        RequireForUpdate<CombatConfig>();
    }

    protected override void OnUpdate()
    {
        var combatConfig = SystemAPI.GetSingleton<CombatConfig>();
        foreach (var (damageBuffer, calculatedDamageBuffer, calculatedStats, entity) in SystemAPI.Query<DynamicBuffer<DamageInstance>, DynamicBuffer<CalculatedDamageInstance>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            foreach(var instance in damageBuffer)
            {
                float calculatedDamage = instance.Damage;
                var stats = calculatedStats.ValueRO.Value;

                switch(instance.Type)
                {
                    case DamageType.Physical:
                    {
                        var physicalResistance = 0.0f;
                        if(SystemAPI.HasComponent<Armor>(entity))
                        {
                            physicalResistance = SystemAPI.GetComponentRO<Armor>(entity).ValueRO.PhysicalResistance;
                        }
                        calculatedDamage = calculatedDamage * (1.0f - physicalResistance);
                        break;
                    }

                    // Elemental: AllResistance adds to each resistance, then Resistance / (Resistance + ElementalResistanceConstant) is mitigated
                    case DamageType.Cold:
                        calculatedDamage *= 1.0f - CombatConfig.Mitigation(stats.ColdResistance + stats.AllResistance, combatConfig.ElementalResistanceConstant);
                    break;
                    case DamageType.Fire:
                        calculatedDamage *= 1.0f - CombatConfig.Mitigation(stats.FireResistance + stats.AllResistance, combatConfig.ElementalResistanceConstant);
                    break;
                    case DamageType.Lightning:
                        calculatedDamage *= 1.0f - CombatConfig.Mitigation(stats.LightningResistance + stats.AllResistance, combatConfig.ElementalResistanceConstant);
                    break;
                    case DamageType.Poison:
                        calculatedDamage *= 1.0f - CombatConfig.Mitigation(stats.PoisonResistance + stats.AllResistance, combatConfig.ElementalResistanceConstant);
                    break;
                }

                if(instance.Hierarchy == DamageHierarchy.Main)
                {
                    if(calculatedStats.ValueRO.Value.BlockChance > 0.0f)
                    {
                        if(_random.NextFloat() < calculatedStats.ValueRO.Value.BlockChance)
                        {
                            float diff = calculatedDamage;
                            calculatedDamage = math.max(0, calculatedDamage - combatConfig.BlockAmount);
                            diff = diff - calculatedDamage;
                            OnDamageBlocked?.Invoke(diff, SystemAPI.GetComponentRO<LocalTransform>(entity).ValueRO.Position);
                        }
                    }
                }
                calculatedDamageBuffer.Add(new CalculatedDamageInstance{Damage = calculatedDamage, Hierarchy = instance.Hierarchy, Type = instance.Type, IsCrit = instance.IsCrit, Source = instance.Source, HitDirection = instance.HitDirection, HitForce = instance.HitForce});
            }
            damageBuffer.Clear();
        }
    }
}


[UpdateBefore(typeof(SubDamageSystemGroup))]
public partial class DamagingSystem : SystemBase
{
    public event System.Action<float, float3, bool> OnDamageTaken;
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    protected override void OnCreate()
    {
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
        RequireForUpdate<StatusEffectConfig>();
    }

    protected override void OnUpdate()
    {
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        var statusEffects = SystemAPI.GetSingleton<StatusEffectConfig>();
        // Optional: without it units just don't flash
        SystemAPI.TryGetSingleton<HitFlashConfig>(out var hitFlashConfig);
        foreach (var (health, calculatedStats, entity) in SystemAPI.Query<RefRW<Health>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            var instances = EntityManager.GetBuffer<CalculatedDamageInstance>(entity);
            foreach(var instance in instances)
            {
                if(instance.Hierarchy == DamageHierarchy.Main)
                {
                    float damage = instance.Damage;
                    var position = EntityManager.GetComponentData<LocalTransform>(entity).Position;
                    if(damage <= 0.0f)
                    {
                        continue;
                    }
                    OnDamageTaken?.Invoke(damage, position, instance.IsCrit);
                    health.ValueRW.CurrentValue -= damage;

                    // Push the target; KnockbackSystem moves it and lets the push die out
                    if(instance.HitForce > 0.0f && SystemAPI.HasComponent<Knockback>(entity))
                    {
                        SystemAPI.GetComponentRW<Knockback>(entity).ValueRW.Velocity += instance.HitDirection * instance.HitForce;
                    }

                    // Flash white, tinted by the damage type
                    if(SystemAPI.HasComponent<HitFlash>(entity))
                    {
                        var flash = SystemAPI.GetComponentRW<HitFlash>(entity);
                        flash.ValueRW.FlashColor = hitFlashConfig.FlashColor(instance.Type);
                        flash.ValueRW.Timer = hitFlashConfig.Duration;
                    }

                    switch(instance.Type)
                    {
                        case DamageType.Physical:
                            // TODO buffers?
                            SystemAPI.GetBuffer<BleedInstance>(entity).Add(new BleedInstance{TimeRemaining = statusEffects.BleedTime});
                        break;
                        case DamageType.Cold:
                            if(!SystemAPI.IsComponentEnabled<Chilled>(entity))
                            {
                                SystemAPI.SetComponentEnabled<Chilled>(entity ,true);
                            }
                            SystemAPI.GetComponentRW<Chilled>(entity).ValueRW.Value += damage * statusEffects.ChilledDamageMultiplier;
                        break;
                        case DamageType.Fire:
                            SystemAPI.GetBuffer<BurnInstance>(entity).Add(new BurnInstance{DamagePerSecond = damage / statusEffects.BurnTime, TimeRemaining = statusEffects.BurnTime});
                        break;
                        case DamageType.Lightning:
                            SystemAPI.GetBuffer<ChargedInstance>(entity).Add(new ChargedInstance{Damage = damage * statusEffects.ChargedDamageMultiplier , Radius = statusEffects.ChargedRadius});
                        break;
                        case DamageType.Poison:
                            if(!SystemAPI.IsComponentEnabled<Poisioned>(entity))
                            {
                                SystemAPI.SetComponentEnabled<Poisioned>(entity ,true);
                            }
                            SystemAPI.GetComponentRW<Poisioned>(entity).ValueRW.TimeRemaining = statusEffects.PoisonTime;
                        break;
                    } 
                }
                else //Subdamage
                {
                    float damage = instance.Damage;
                    if(damage <= 0.0f)
                    {
                        continue;
                    }

                    switch(instance.Type)
                    {
                        case DamageType.Physical:
                        break;
                        case DamageType.Cold:
                        break;
                        case DamageType.Fire:
                        break;
                        case DamageType.Lightning:
                            // only show 
                            OnDamageTaken?.Invoke(damage, EntityManager.GetComponentData<LocalTransform>(entity).Position, instance.IsCrit);
                        break;
                        case DamageType.Poison:
                        break;
                    }
                    health.ValueRW.CurrentValue -= damage;
                }
            }
            instances.Clear();
        }
    }
}

public partial class ApplyHealingSystem : SystemBase
{
    public event System.Action<float, float3> OnHealingReceived;
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    protected override void OnCreate()
    {
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
    }

    protected override void OnUpdate()
    {
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        foreach (var (health, calculatedStats, entity) in SystemAPI.Query<RefRW<Health>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            var instances = EntityManager.GetBuffer<HealingInstance>(entity);
            foreach(var instance in instances)
            {
                float heal = instance.Value;
                var position = EntityManager.GetComponentData<LocalTransform>(entity).Position;
                if(heal <= 0.0f)
                {
                    continue;
                }
                OnHealingReceived?.Invoke(heal, position);
                health.ValueRW.CurrentValue += heal;
            }
            instances.Clear();
        }
    }
}
