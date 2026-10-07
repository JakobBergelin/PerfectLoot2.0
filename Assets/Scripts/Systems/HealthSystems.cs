using Unity.Burst;
using Unity.Entities;
using UnityEngine;
using Unity.Collections;
using Unity.Transforms;
using Unity.Mathematics;

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
    protected override void OnUpdate()
    {
        foreach (var (damageBuffer, calculatedDamageBuffer, calculatedStats, calculatedStatsPercentages, entity) in SystemAPI.Query<DynamicBuffer<DamageInstance>, DynamicBuffer<CalculatedDamageInstance>, RefRO<CalculatedStats>, RefRO<CalculatedStatsPercentages>>().WithEntityAccess())
        {
            foreach(var instance in damageBuffer)
            {
                float calculatedDamage = instance.Damage;

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

                    case DamageType.Cold:
                        calculatedDamage = calculatedDamage * (1.0f - calculatedStatsPercentages.ValueRO.ColdResitance);
                    break;
                    case DamageType.Fire:
                        calculatedDamage = calculatedDamage * (1.0f - calculatedStatsPercentages.ValueRO.FireResistance);
                    break;
                    case DamageType.Lightning:
                        calculatedDamage = calculatedDamage * (1.0f - calculatedStatsPercentages.ValueRO.LightningResistance);
                    break;
                    case DamageType.Poison:
                        calculatedDamage = calculatedDamage * (1.0f - calculatedStatsPercentages.ValueRO.PoisonResistance);
                    break;
                }
                calculatedDamage = calculatedDamage * (1.0f - calculatedStatsPercentages.ValueRO.AllResistance);

                if(instance.Hierarchy == DamageHierarchy.Main)
                {
                    if(calculatedStatsPercentages.ValueRO.BlockChance > 0.0f)
                    {
                        if(UnityEngine.Random.Range(0.0f,1.0f) < calculatedStatsPercentages.ValueRO.BlockChance)
                        {
                            float diff = calculatedDamage;
                            calculatedDamage = math.max(0, calculatedDamage - Constants.BlockAmount);
                            diff = diff - calculatedDamage;
                            OnDamageBlocked?.Invoke(diff, SystemAPI.GetComponentRO<LocalTransform>(entity).ValueRO.Position);
                        }
                    }
                }
                calculatedDamageBuffer.Add(new CalculatedDamageInstance{Damage = calculatedDamage, Hierarchy = instance.Hierarchy, Type = instance.Type, IsCrit = instance.IsCrit, Source = instance.Source});
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
    }

    protected override void OnUpdate()
    {
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        foreach (var (health, calculatedStatsPercentages, entity) in SystemAPI.Query<RefRW<Health>, RefRO<CalculatedStatsPercentages>>().WithEntityAccess())
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

                    switch(instance.Type)
                    {
                        case DamageType.Physical:
                            // TODO buffers?
                            SystemAPI.GetBuffer<BleedInstance>(entity).Add(new BleedInstance{TimeRemaining = Constants.DefaultBleedTime});
                        break;
                        case DamageType.Cold:
                            if(!SystemAPI.IsComponentEnabled<Chilled>(entity))
                            {
                                SystemAPI.SetComponentEnabled<Chilled>(entity ,true);
                            }
                            SystemAPI.GetComponentRW<Chilled>(entity).ValueRW.Value += damage * Constants.DefaultChilledDamageMultiplier;
                        break;
                        case DamageType.Fire:
                            SystemAPI.GetBuffer<BurnInstance>(entity).Add(new BurnInstance{DamagePerSecond = damage / Constants.DefaultBurnTime, TimeRemaining = Constants.DefaultBurnTime});
                        break;
                        case DamageType.Lightning:
                            SystemAPI.GetBuffer<ChargedInstance>(entity).Add(new ChargedInstance{Damage = damage * Constants.DefaultChargedDamageMultiplier , Radius = Constants.DefaultChargedRadius});
                        break;
                        case DamageType.Poison:
                            if(!SystemAPI.IsComponentEnabled<Poisioned>(entity))
                            {
                                SystemAPI.SetComponentEnabled<Poisioned>(entity ,true);
                            }
                            SystemAPI.GetComponentRW<Poisioned>(entity).ValueRW.TimeRemaining = Constants.DefaultPoisonTime;
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

[BurstCompile]
[UpdateInGroup(typeof(CalculateStatsSystemGroup), OrderLast = true)]
public partial struct MultiplyArmorSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var armor in SystemAPI.Query<RefRW<Armor>>())
        {
            if(armor.ValueRO.Value == 0)
            {
                armor.ValueRW.PhysicalResistance = 0.0f;
            }
            else
            {
                armor.ValueRW.PhysicalResistance = 1.0f - math.pow(1.0f - (Constants.ReductionPerArmor * (1.0f + armor.ValueRO.PhysicalResistanceMultiplier)), armor.ValueRO.Value);
            }
        }
    }
}

[BurstCompile]
[UpdateInGroup(typeof(CalculateStatsSystemGroup), OrderLast = true)]
public partial struct MultiplyHealthSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var health in SystemAPI.Query<RefRW<Health>>())
        {
            health.ValueRW.MaxValue *=  1.0f + health.ValueRO.MaxHealthMultiplier;
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
        foreach (var (health, calculatedStatsPercentages, entity) in SystemAPI.Query<RefRW<Health>, RefRO<CalculatedStatsPercentages>>().WithEntityAccess())
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
