using UnityEngine;
using Unity.Entities;

public class BaseStatsAuthoring : MonoBehaviour
{
    public float Strength;
    public float Agility;
    public float Intelligence;
    public float Health;
    public float Resource;
    public float Lifesteal;
    public float HealthRegeneration;
    public float ResourceRegeneration;
    public int Pierce;
    public int Bounces;
    public int Armor;
    public float MovementSpeed;
    public float AreaOfEffect;
    public float CooldownReduction; 

    public float PhysicalResistance;
    public float ColdResitance;
    public float FireResistance;
    public float PoisonResistance;
    public float LightningResistance;
    public float AllResistance;
    public float CriticalChance;
    public float CriticalDamage;
    public float BlockChance;


    public class Baker : Baker<BaseStatsAuthoring>
    {
        public override void Bake(BaseStatsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new BaseStats
            {
                Strength = authoring.Strength, 
                Agility = authoring.Agility, 
                Intelligence = authoring.Intelligence, 
                Health = authoring.Health, 
                Resource = authoring.Resource, 
                Lifesteal = authoring.Lifesteal, 
                HealthRegeneration = authoring.HealthRegeneration, 
                ResourceRegeneration = authoring.ResourceRegeneration, 
                Pierce = authoring.Pierce,
                Bounces = authoring.Bounces,
                Armor = authoring.Armor
            });
            AddComponent(entity, new BaseStatsPercentages
            {
                MovementSpeed = authoring.MovementSpeed, 
                AreaOfEffect = authoring.AreaOfEffect, 
                CooldownReduction = authoring.CooldownReduction, 
                ColdResitance = authoring.ColdResitance, 
                FireResistance = authoring.FireResistance, 
                PoisonResistance = authoring.PoisonResistance, 
                LightningResistance = authoring.LightningResistance, 
                AllResistance = authoring.AllResistance, 
                CriticalChance = authoring.CriticalChance,
                CriticalDamage = authoring.CriticalDamage,
                BlockChance = authoring.BlockChance
            });
            AddComponent(entity, new CalculatedStats{});
            AddComponent(entity, new CalculatedStatsPercentages{});
            AddComponent(entity, new MovementSpeed{});
            AddComponent(entity, new Armor{});
        }
    }
}

public struct BaseStats : IComponentData
{
    public float Strength;
    public float Agility;

    public float Intelligence;
    public float WeaponDamage;
    public float Health;
    public float Resource;
    public float Lifesteal;
    public float HealthRegeneration;
    public float ResourceRegeneration;

    public int Pierce;
    public int Bounces;
    public int Armor;

}

public struct MovementSpeed : IComponentData
{
    public float Value;
}

public struct CalculatedStats : IComponentData
{
    public float Strength;
    public float Agility;
    public float Intelligence;
    public float WeaponDamage;
    public float WeaponDamageRange;
    public float AttackSpeed;
    public float Lifesteal;
    public int Pierce;
    public int Bounces;
    public bool PierceOnCrit;
}

public struct Armor : IComponentData
{
    public int Value;
    public float PhysicalResistanceMultiplier;

    public float PhysicalResistance;
}

public struct BaseStatsPercentages : IComponentData
{
    public float MovementSpeed;
    public float Damage;
    public float AreaOfEffect;
    public float CooldownReduction;
    public float ColdResitance;
    public float FireResistance;
    public float PoisonResistance;
    public float LightningResistance;
    public float AllResistance;
    public float CriticalChance;
    public float CriticalDamage;
    public float BlockChance;
}

public struct CalculatedStatsPercentages : IComponentData
{
    public float Damage;
    public float AreaOfEffect;
    public float CooldownReduction;
    public float ColdResitance;
    public float FireResistance;
    public float PoisonResistance;
    public float LightningResistance;
    public float AllResistance;
    public float CriticalChance;
    public float CriticalDamage;
    public float BlockChance;
}
