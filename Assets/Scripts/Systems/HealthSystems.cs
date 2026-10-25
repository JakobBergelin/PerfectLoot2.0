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
// (skills, movement; physics runs first in the frame) and before the transforms are calculated: FeedbackSystemGroup
[UpdateInGroup(typeof(FeedbackSystemGroup))]
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

[UpdateInGroup(typeof(HealthAndResourceSystemGroup))]
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
// Keeps CurrentValue at or below MaxValue. FinalizeStatsSystem lowers MaxValue without touching CurrentValue (an
// item with +Health unequipped, a buff ending, Golem's multiplier), so this runs after all healing and regeneration
// and before the health bars read it (FeedbackSystemGroup). Only clamps from above: a lower max doesn't kill.
[UpdateInGroup(typeof(HealthAndResourceSystemGroup), OrderLast = true)]
partial struct ClampHealthSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var health in SystemAPI.Query<RefRW<Health>>())
        {
            if (health.ValueRO.CurrentValue > health.ValueRO.MaxValue)
                health.ValueRW.CurrentValue = health.ValueRO.MaxValue;
        }
    }
}

[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
partial struct CheckDeathSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var toDestroy = new NativeList<Entity>(Allocator.Temp);
        var toAnimate = new NativeList<Entity>(Allocator.Temp);
        var animators = SystemAPI.GetComponentLookup<RigidAnimator>(true);
        foreach (var (health, entity) in SystemAPI.Query<RefRO<Health>>().WithEntityAccess())
        {
            if(health.ValueRO.CurrentValue <= 0.0f)
            {
                // Rigid models with a Death clip play it first (RigidDeathSystem destroys them afterwards)
                if (animators.HasComponent(entity) && RigidDeathUtility.HasDeathClip(animators[entity])) toAnimate.Add(entity);
                else toDestroy.Add(entity);
            }
        }

        if(!toDestroy.IsEmpty)
        {
            state.EntityManager.AddComponent<DestroyWithChildren>(toDestroy.AsArray());
        }
        if (!toAnimate.IsEmpty)
        {
            RigidDeathUtility.StartDying(state.EntityManager, toAnimate.AsArray());
        }
    }
}


[UpdateInGroup(typeof(DamageSystemGroup))]
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
            if (damageBuffer.IsEmpty) continue;

            // Buffs that stop damage
            bool invulnerable = SystemAPI.HasComponent<Invulnerable>(entity) && SystemAPI.IsComponentEnabled<Invulnerable>(entity);
            bool hasRefraction = SystemAPI.HasComponent<Refraction>(entity) && SystemAPI.IsComponentEnabled<Refraction>(entity);

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

                // Vanguard: main physical hits lose a flat amount after armor, down to 0 (before Refraction, so a hit
                // Vanguard cancels doesn't use up a stack, and before block)
                if(instance.Type == DamageType.Physical && instance.Hierarchy == DamageHierarchy.Main && SystemAPI.HasComponent<Vanguard>(entity))
                {
                    calculatedDamage = math.max(0.0f, calculatedDamage - SystemAPI.GetComponent<Vanguard>(entity).DamageReduction);
                }

                // Invulnerable: no damage at all. Refraction: absorbs one hit per stack (shown as blocked).
                if(invulnerable)
                {
                    calculatedDamage = 0.0f;
                }
                else if(hasRefraction && instance.Hierarchy == DamageHierarchy.Main && calculatedDamage > 0.0f)
                {
                    // Use up one stack; the last one ends the effect
                    var refraction = SystemAPI.GetComponentRW<Refraction>(entity);
                    refraction.ValueRW.Stacks--;
                    if(refraction.ValueRO.Stacks <= 0)
                    {
                        SystemAPI.SetComponentEnabled<Refraction>(entity, false);
                        hasRefraction = false;
                    }
                    OnDamageBlocked?.Invoke(calculatedDamage, SystemAPI.GetComponentRO<LocalTransform>(entity).ValueRO.Position);
                    calculatedDamage = 0.0f;
                }

                if(instance.Hierarchy == DamageHierarchy.Main && calculatedDamage > 0.0f)
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


[UpdateInGroup(typeof(DamageSystemGroup))]
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
        var bleeds = SystemAPI.GetBufferLookup<BleedInstance>();
        var bleeding = SystemAPI.GetComponentLookup<Bleeding>();
        var burns = SystemAPI.GetBufferLookup<BurnInstance>();
        var burning = SystemAPI.GetComponentLookup<Burning>();
        var poisoned = SystemAPI.GetComponentLookup<Poisoned>();
        var chilled = SystemAPI.GetComponentLookup<Chilled>();
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
                        // HitForce is an impulse: heavier units are pushed less (mass 1 = HitForce as speed)
                        float inverseMass = SystemAPI.HasComponent<Unity.Physics.PhysicsMass>(entity) ? SystemAPI.GetComponent<Unity.Physics.PhysicsMass>(entity).InverseMass : 1.0f;
                        SystemAPI.GetComponentRW<Knockback>(entity).ValueRW.Velocity += instance.HitDirection * instance.HitForce * inverseMass;
                    }

                    // Flash white, tinted by the damage type
                    if(SystemAPI.HasComponent<HitFlash>(entity))
                    {
                        var flash = SystemAPI.GetComponentRW<HitFlash>(entity);
                        flash.ValueRW.FlashColor = hitFlashConfig.FlashColor(instance.Type);
                        flash.ValueRW.Timer = hitFlashConfig.Duration;
                    }

                    // Status effect per damage type
                    switch(instance.Type)
                    {
                        case DamageType.Physical:
                            EffectApply.AddBleed(bleeds, bleeding, entity, statusEffects.BleedDuration, instance.Source);
                        break;
                        case DamageType.Cold:
                            EffectApply.AddChill(chilled, entity, math.max(1, (int)math.round(damage * statusEffects.ChilledDamageMultiplier)), statusEffects.ChillMaxStacks, statusEffects.ChillDuration);
                        break;
                        case DamageType.Fire:
                            // The hit's damage is dealt again over the burn's duration
                            if(statusEffects.BurnDuration > 0.0f)
                                EffectApply.AddBurn(burns, burning, entity, damage / statusEffects.BurnDuration, statusEffects.BurnDuration, instance.Source);
                        break;
                        case DamageType.Lightning:
                            SystemAPI.GetBuffer<ChargedInstance>(entity).Add(new ChargedInstance{Damage = damage * statusEffects.ChargedDamageMultiplier , Radius = statusEffects.ChargedRadius});
                        break;
                        case DamageType.Poison:
                            if(poisoned.HasComponent(entity))
                            {
                                var poison = EffectApply.Refresh(poisoned, entity, statusEffects.PoisonDuration);
                                poison.ValueRW.DamagePerSecond = statusEffects.PoisonDamagePerSecond;
                                poison.ValueRW.Source = instance.Source;
                            }
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

[UpdateInGroup(typeof(HealthAndResourceSystemGroup))]
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
                // Never above max health; the combat text shows what was actually restored
                float heal = math.min(instance.Value, health.ValueRO.MaxValue - health.ValueRO.CurrentValue);
                if(heal <= 0.0f)
                {
                    continue;
                }
                var position = EntityManager.GetComponentData<LocalTransform>(entity).Position;
                OnHealingReceived?.Invoke(heal, position);
                health.ValueRW.CurrentValue += heal;
            }
            instances.Clear();
        }
    }
}
