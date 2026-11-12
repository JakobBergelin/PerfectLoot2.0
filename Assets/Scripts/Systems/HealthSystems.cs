using Unity.Burst;
using Unity.Entities;
using UnityEngine;
using Unity.Collections;
using Unity.Transforms;
using Unity.Mathematics;

// Health bars over units: how full, and the colour by side (UnitBar.shader). Players' are hidden, as UnitBarHUD draws
// theirs with the values. Counter-rotates the bar against the unit's rotation, so it has to run after everything that
// rotates units (skills, movement; physics runs first in the frame) and before the transforms are calculated: FeedbackSystemGroup
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

        var bars = SystemAPI.GetComponentLookup<UnitBarParams>();
        var players = SystemAPI.GetComponentLookup<Player>(true);
        var companions = SystemAPI.GetComponentLookup<Companion>(true);
        var barriers = SystemAPI.GetComponentLookup<Barrier>(true);
        foreach (var (healthBar, health, localTransform, entity) in SystemAPI.Query<RefRO<HealthBarUI>, RefRO<Health>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            float kind = players.HasComponent(entity) ? UnitBarKind.Hidden : companions.HasComponent(entity) ? UnitBarKind.Companion : UnitBarKind.Enemy;
            // z: the barrier, as a share of max health, drawn over the bar
            float barrier = barriers.HasComponent(entity) ? barriers[entity].Value / health.ValueRO.MaxValue : 0f;
            if (bars.HasComponent(healthBar.ValueRO.BarEntity))
                bars[healthBar.ValueRO.BarEntity] = new UnitBarParams { Value = new float4(health.ValueRO.CurrentValue / health.ValueRO.MaxValue, kind, barrier, 0f) };

            var stickTransform = SystemAPI.GetComponentRW<LocalTransform>(healthBar.ValueRO.StickEntity);
            stickTransform.ValueRW.Rotation = localTransform.ValueRO.InverseTransformRotation(quaternion.LookRotation(cameraForward, math.up()));
        }
    }
}

// Cold Blooded wearers don't regenerate: ColdBloodedSystem deals their regeneration as damage instead
[UpdateInGroup(typeof(HealthAndResourceSystemGroup))]
partial struct HealthRegenerationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var meter = new DamageMeterRecorder(ref state, SystemAPI.GetSingleton<DamageMeter>());
        foreach (var (health, healthRegeneration, entity) in SystemAPI.Query<RefRW<Health>, RefRO<HealthRegeneration>>().WithAbsent<Golem>().WithNone<ColdBlooded>().WithEntityAccess())
        {
            float before = health.ValueRO.CurrentValue;
            health.ValueRW.CurrentValue += healthRegeneration.ValueRO.Value * SystemAPI.Time.DeltaTime;
            health.ValueRW.CurrentValue = Mathf.Min(health.ValueRO.CurrentValue, health.ValueRO.MaxValue);
            float healed = health.ValueRO.CurrentValue - before;
            if (healed > 0.0f) meter.Healing(entity, entity, DamageLabel.Regeneration, healed);
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
    public event System.Action<float3> OnDamageDodged;
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
                            var armor = SystemAPI.GetComponentRO<Armor>(entity).ValueRO;
                            physicalResistance = armor.PhysicalResistance;
                            // The attacker's ArmorPierce ignores some of the armor (none when the attacker is gone)
                            float pierce = SystemAPI.HasComponent<CalculatedStats>(instance.Source)
                                ? SystemAPI.GetComponent<CalculatedStats>(instance.Source).Value.ArmorPierce : 0.0f;
                            if(pierce > 0.0f)
                                physicalResistance = CombatConfig.PhysicalResistance(armor.Value, armor.PhysicalResistanceMultiplier, pierce, combatConfig.ArmorConstant);
                        }
                        calculatedDamage = calculatedDamage * (1.0f - physicalResistance);
                        break;
                    }

                    // Elemental: AllResistance adds to each resistance, then ElementalMitigation (negative resistance takes extra damage)
                    case DamageType.Cold:
                        calculatedDamage *= 1.0f - CombatConfig.ElementalMitigation(stats.ColdResistance + stats.AllResistance, combatConfig.ElementalResistanceConstant);
                    break;
                    case DamageType.Fire:
                        calculatedDamage *= 1.0f - CombatConfig.ElementalMitigation(stats.FireResistance + stats.AllResistance, combatConfig.ElementalResistanceConstant);
                    break;
                    case DamageType.Lightning:
                        calculatedDamage *= 1.0f - CombatConfig.ElementalMitigation(stats.LightningResistance + stats.AllResistance, combatConfig.ElementalResistanceConstant);
                    break;
                    case DamageType.Poison:
                        calculatedDamage *= 1.0f - CombatConfig.ElementalMitigation(stats.PoisonResistance + stats.AllResistance, combatConfig.ElementalResistanceConstant);
                    break;
                }

                // Projectile and melee damage reduction, after armor and resistances: a projectile's hit, or an enemy's
                // melee hit (no cap: 100% or more takes nothing)
                if(instance.Attack != Entity.Null)
                {
                    if(SystemAPI.HasComponent<Projectile>(instance.Attack))
                        calculatedDamage *= math.max(0.0f, 1.0f - stats.ProjectileDamageReduction);
                    else if(SystemAPI.HasComponent<EnemyMeleeHit>(instance.Attack))
                        calculatedDamage *= math.max(0.0f, 1.0f - stats.MeleeDamageReduction);
                }

                // Vanguard: main physical hits lose a flat amount after armor, down to 0 (before Refraction, so a hit
                // Vanguard cancels doesn't use up a stack, and before block)
                if(instance.Type == DamageType.Physical && instance.Hierarchy == DamageHierarchy.Main && SystemAPI.HasComponent<Vanguard>(entity))
                {
                    calculatedDamage = math.max(0.0f, calculatedDamage - SystemAPI.GetComponent<Vanguard>(entity).DamageReduction);
                }

                // Invulnerable: no damage at all. Dodge: a main hit misses, before it can use up a Refraction stack (Vigil
                // heals). Refraction: absorbs one hit per stack (shown as blocked).
                if(invulnerable)
                {
                    calculatedDamage = 0.0f;
                }
                else if(instance.Hierarchy == DamageHierarchy.Main && calculatedDamage > 0.0f && stats.DodgeChance > 0.0f && _random.NextFloat() < stats.DodgeChance)
                {
                    OnDamageDodged?.Invoke(SystemAPI.GetComponentRO<LocalTransform>(entity).ValueRO.Position);
                    calculatedDamage = 0.0f;
                    if(SystemAPI.HasComponent<HitsAvoided>(entity)) SystemAPI.GetComponentRW<HitsAvoided>(entity).ValueRW.Count++;
                    if(SystemAPI.HasComponent<Vigil>(entity))
                    {
                        SystemAPI.GetBuffer<HealingInstance>(entity).Add(new HealingInstance{Value = SystemAPI.GetComponent<Vigil>(entity).HealAmount, Source = entity, Label = DamageLabel.Vigil});
                    }
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
                            if(SystemAPI.HasComponent<HitsAvoided>(entity)) SystemAPI.GetComponentRW<HitsAvoided>(entity).ValueRW.Count++;
                        }
                    }
                }
                calculatedDamageBuffer.Add(new CalculatedDamageInstance{Origin = instance.Origin, Damage = calculatedDamage, Hierarchy = instance.Hierarchy, Type = instance.Type, IsCrit = instance.IsCrit, Source = instance.Source, HitDirection = instance.HitDirection, HitForce = instance.HitForce, IsCenterTarget = instance.IsCenterTarget, Attack = instance.Attack});
            }
            damageBuffer.Clear();
        }
    }
}


[UpdateInGroup(typeof(DamageSystemGroup))]
public partial class DamagingSystem : SystemBase
{
    public event System.Action<float, float3, bool> OnDamageTaken;
    /// <summary>A Barrier soaked a whole hit (amount, position).</summary>
    public event System.Action<float, float3> OnDamageAbsorbed;
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    protected override void OnCreate()
    {
        _beginInitializationEntityCommandBufferSystem = World.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
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
        double elapsedTime = SystemAPI.Time.ElapsedTime;
        // Optional: without it units just don't flash
        SystemAPI.TryGetSingleton<HitFlashConfig>(out var hitFlashConfig);
        var meter = new DamageMeterRecorder(ref CheckedStateRef, SystemAPI.GetSingleton<DamageMeter>());
        var barriers = SystemAPI.GetComponentLookup<Barrier>();
        foreach (var (health, calculatedStats, entity) in SystemAPI.Query<RefRW<Health>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            var instances = EntityManager.GetBuffer<CalculatedDamageInstance>(entity);
            bool hasBarrier = barriers.HasComponent(entity);
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
                    // A Barrier soaks it first. A hit it soaks whole does nothing at all (no push, no status);
                    // statuses of a hit that gets through come from what got through
                    if(hasBarrier && barriers[entity].Value > 0.0f)
                    {
                        damage = BarrierUtil.Absorb(ref barriers.GetRefRW(entity).ValueRW, damage);
                        if(damage <= 0.0f)
                        {
                            OnDamageAbsorbed?.Invoke(instance.Damage, position);
                            continue;
                        }
                    }
                    OnDamageTaken?.Invoke(damage, position, instance.IsCrit);
                    health.ValueRW.CurrentValue -= damage;
                    meter.Damage(entity, instance.Source, instance.Origin, damage);
                    if(SystemAPI.HasComponent<LastHit>(entity))
                    {
                        var lastHit = new LastHit{Direction = instance.HitDirection, Source = instance.Source, Time = elapsedTime, Type = instance.Type, HitForce = instance.HitForce};
                        SystemAPI.SetComponent(entity, lastHit);
                    }

                    // Push the target, or pull it if negative (Undertow); KnockbackSystem moves it and lets the push die out
                    if(instance.HitForce != 0.0f && SystemAPI.HasComponent<Knockback>(entity))
                    {
                        // HitForce is an impulse: heavier units are pushed less (mass 1 = HitForce as speed)
                        float inverseMass = SystemAPI.HasComponent<BodyMass>(entity) ? SystemAPI.GetComponent<BodyMass>(entity).InverseMass : 1.0f;
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
                            // One charge per attack: only its center target. The jump radius grows with the attacker's area of effect
                            if(!instance.IsCenterTarget) break;
                            float chargedRadius = statusEffects.ChargedRadius * (SystemAPI.HasComponent<CalculatedStats>(instance.Source) ? SystemAPI.GetComponent<CalculatedStats>(instance.Source).Value.AreaMultiplier : 1f);
                            SystemAPI.GetBuffer<ChargedInstance>(entity).Add(new ChargedInstance{Damage = damage * statusEffects.ChargedDamageMultiplier , Radius = chargedRadius, Source = instance.Source});
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
                    if(hasBarrier && damage > 0.0f && barriers[entity].Value > 0.0f) damage = BarrierUtil.Absorb(ref barriers.GetRefRW(entity).ValueRW, damage);
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
                    meter.Damage(entity, instance.Source, instance.Origin, damage);
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
        _beginInitializationEntityCommandBufferSystem = World.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
    }

    protected override void OnUpdate()
    {
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        var meter = new DamageMeterRecorder(ref CheckedStateRef, SystemAPI.GetSingleton<DamageMeter>());
        var overflows = SystemAPI.GetComponentLookup<Overflow>(true);
        var barriers = SystemAPI.GetComponentLookup<Barrier>();
        foreach (var (health, calculatedStats, entity) in SystemAPI.Query<RefRW<Health>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            var instances = EntityManager.GetBuffer<HealingInstance>(entity);
            foreach(var instance in instances)
            {
                // Never above max health; the combat text shows what was actually restored
                float heal = math.min(instance.Value, health.ValueRO.MaxValue - health.ValueRO.CurrentValue);

                // Overflow: what a healer with it heals past full health becomes a barrier on the one healed
                float extra = instance.Value - math.max(0.0f, heal);
                if(extra > 0.0f && overflows.HasComponent(instance.Source) && barriers.HasComponent(entity))
                {
                    var overflow = overflows[instance.Source];
                    BarrierUtil.Add(ref barriers.GetRefRW(entity).ValueRW, extra, health.ValueRO.MaxValue * overflow.MaxShare, overflow.Duration);
                }

                if(heal <= 0.0f)
                {
                    continue;
                }
                var position = EntityManager.GetComponentData<LocalTransform>(entity).Position;
                OnHealingReceived?.Invoke(heal, position);
                health.ValueRW.CurrentValue += heal;
                meter.Healing(entity, instance.Source, instance.Label, heal);
            }
            instances.Clear();
        }
    }
}
