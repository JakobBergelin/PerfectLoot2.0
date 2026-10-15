using UnityEngine;
using UnityEngine.Serialization;
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
    [FormerlySerializedAs("ColdResitance")]
    public float ColdResistance;
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
                Value = new StatBlock
                {
                    Strength = authoring.Strength,
                    Agility = authoring.Agility,
                    Intelligence = authoring.Intelligence,
                    AttackSpeed = 1.0f,
                    Health = authoring.Health,
                    HealthRegeneration = authoring.HealthRegeneration,
                    Resource = authoring.Resource,
                    ResourceRegeneration = authoring.ResourceRegeneration,
                    Lifesteal = authoring.Lifesteal,
                    Pierce = authoring.Pierce,
                    Bounces = authoring.Bounces,
                    Armor = authoring.Armor,
                    MovementSpeed = authoring.MovementSpeed,
                    AreaOfEffect = authoring.AreaOfEffect,
                    CooldownReduction = authoring.CooldownReduction,
                    PhysicalResistance = authoring.PhysicalResistance,
                    ColdResistance = authoring.ColdResistance,
                    FireResistance = authoring.FireResistance,
                    PoisonResistance = authoring.PoisonResistance,
                    LightningResistance = authoring.LightningResistance,
                    AllResistance = authoring.AllResistance,
                    CriticalChance = authoring.CriticalChance,
                    CriticalDamage = authoring.CriticalDamage,
                    BlockChance = authoring.BlockChance
                }
            });
            AddComponent(entity, new EquipmentStats{});
            AddComponent(entity, new CalculatedStats{});
            AddComponent(entity, new MovementSpeed{});
            AddComponent(entity, new Armor{});
        }
    }
}

/// <summary>
/// Every stat a unit can have. Shared by BaseStats, EquipmentStats and CalculatedStats.
/// Percentages are fractions: 0.1 = 10%.
/// </summary>
[System.Serializable]
public struct StatBlock
{
    public float Strength;
    public float Agility;
    public float Intelligence;

    public float WeaponDamage;
    public float WeaponDamageRange;     // Weapon damage rolls are WeaponDamage +- this
    public float WeaponReach;           // How far in front of the unit melee attacks land; Constants.UnarmedWeaponReach when 0
    public float AttackSpeed;
    public float Damage;

    public float Health;
    public float HealthRegeneration;
    public float Resource;
    public float ResourceRegeneration;
    public float ResourceCostReduction;
    public float ResourceSteal;
    public float Lifesteal;

    public int Pierce;
    public int Bounces;
    public int Armor;

    public float MovementSpeed;
    public float AreaOfEffect;
    public float CooldownReduction;

    public float PhysicalResistance;
    public float ColdResistance;
    public float FireResistance;
    public float PoisonResistance;
    public float LightningResistance;
    public float AllResistance;

    public float CriticalChance;
    public float CriticalDamage;
    public float BlockChance;
    public float DodgeChance;

    public void Add(in StatBlock other)
    {
        Strength += other.Strength;
        Agility += other.Agility;
        Intelligence += other.Intelligence;

        WeaponDamage += other.WeaponDamage;
        WeaponDamageRange += other.WeaponDamageRange;
        WeaponReach += other.WeaponReach;
        AttackSpeed += other.AttackSpeed;
        Damage += other.Damage;

        Health += other.Health;
        HealthRegeneration += other.HealthRegeneration;
        Resource += other.Resource;
        ResourceRegeneration += other.ResourceRegeneration;
        ResourceCostReduction += other.ResourceCostReduction;
        ResourceSteal += other.ResourceSteal;
        Lifesteal += other.Lifesteal;

        Pierce += other.Pierce;
        Bounces += other.Bounces;
        Armor += other.Armor;

        MovementSpeed += other.MovementSpeed;
        AreaOfEffect += other.AreaOfEffect;
        CooldownReduction += other.CooldownReduction;

        PhysicalResistance += other.PhysicalResistance;
        ColdResistance += other.ColdResistance;
        FireResistance += other.FireResistance;
        PoisonResistance += other.PoisonResistance;
        LightningResistance += other.LightningResistance;
        AllResistance += other.AllResistance;

        CriticalChance += other.CriticalChance;
        CriticalDamage += other.CriticalDamage;
        BlockChance += other.BlockChance;
        DodgeChance += other.DodgeChance;
    }
}

public struct BaseStats : IComponentData
{
    public StatBlock Value;
}

/// <summary>Summed stats of all equipped items. Written by InventoryManager when equipment changes.</summary>
public struct EquipmentStats : IComponentData
{
    public StatBlock Value;
}

/// <summary>Reset to BaseStats every frame, then built up by the systems in CalculateStatsSystemGroup.</summary>
public struct CalculatedStats : IComponentData
{
    public StatBlock Value;
    public bool PierceOnCrit;
}

public struct MovementSpeed : IComponentData
{
    public float Value;
}

public struct Armor : IComponentData
{
    public int Value;
    public float PhysicalResistanceMultiplier;

    public float PhysicalResistance;
}
