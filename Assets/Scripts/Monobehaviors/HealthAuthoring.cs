using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using System;

public class HealthAuthoring : MonoBehaviour
{
    public GameObject HealthBarPivotGameObject;
    public GameObject HealthBarStickGameObject;
    public class Baker : Baker<HealthAuthoring>
    {
        public override void Bake(HealthAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Health{MaxValue = 1.0f, CurrentValue=1.0f});
            AddComponent(entity, new HealthRegeneration());
            AddComponent(entity, new HealthBarUI{PivotEntity = GetEntity(authoring.HealthBarPivotGameObject, TransformUsageFlags.NonUniformScale), 
                StickEntity = GetEntity(authoring.HealthBarStickGameObject, TransformUsageFlags.NonUniformScale)});

            AddBuffer<DamageInstance>(entity);
            AddBuffer<HealingInstance>(entity);
            AddBuffer<PeriodicDamageInstance>(entity);
            AddBuffer<CalculatedDamageInstance>(entity);
            AddComponent(entity, new Knockback());
            
            AddComponent(entity, new Chilled());
            SetComponentEnabled<Chilled>(entity, false);

            AddComponent(entity, new Frozen());
            SetComponentEnabled<Frozen>(entity, false);
            
            AddBuffer<BurnInstance>(entity);
            AddComponent(entity, new Burning());
            SetComponentEnabled<Burning>(entity, false);

            AddBuffer<BleedInstance>(entity);
            AddComponent(entity, new Bleeding());
            SetComponentEnabled<Bleeding>(entity, false);

            AddComponent(entity, new Poisioned());
            SetComponentEnabled<Poisioned>(entity, false);

            AddBuffer<ChargedInstance>(entity);
        }
    }
}

public struct Health : IComponentData
{
    public float MaxValue;
    public float CurrentValue;
    public float MaxHealthMultiplier;
}

public struct HealthBarUI : IComponentData
{
    public Entity PivotEntity;
    public Entity StickEntity;
}

public struct HealthRegeneration : IComponentData
{
    public float Value;
}

public struct DamageInstance : IBufferElementData
{    
    public float Damage;
    public DamageType Type;
    public DamageHierarchy Hierarchy;
    public Entity Source;
    public bool IsCrit;
    public float3 HitDirection;     // Normalized, away from the attack; with HitForce, pushes the target (knockback, effects)
    public float HitForce;
}

public struct HealingInstance : IBufferElementData
{    
    public float Value;
    public Entity Source;
}

public struct PeriodicDamageInstance : IBufferElementData
{    
    public float AccumilatedDamage;
    public DamageType Type;
    public DamageHierarchy Hierarchy;
    public SkillType Skill;
    public float Tick;
    public Entity Source;
    public float3 HitDirection;     // Away from the attack, updated every frame it overlaps
    public float HitForce;
}

public struct CalculatedDamageInstance : IBufferElementData
{    
    public float Damage;
    public DamageType Type;
    public DamageHierarchy Hierarchy;
    public Entity Source;
    public bool IsCrit;
    public float3 HitDirection;
    public float HitForce;
}