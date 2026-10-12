using System;
using System.Linq;
using Unity.Entities;
using UnityEngine;

public struct InventoryItem : IBufferElementData
{
    [SerializeField]
    public Guid Seed;
}


public struct Vanguard : IComponentData
{
    public float Health;
    // Blocks all damage without block chance
    public float BlockAmount;
}

public struct NormalBoots : IComponentData
{
    public float MovementSpeed;
    public int Armor;
}

public enum BootsAffixType
{
    ColdResitance,
    FireResistance,
    PoisonResistance,
    LightningResistance,
    AllResistance,
    BlockChance,
    MobilitySkillCooldownReduction,
    Agility,
    ResourceCostReduction,
    DodgeChance,

}

public struct BootsAffix
{
    public BootsAffixType Type;

    public float Value;

    public static BootsAffix RandomizeAffix(System.Random random, float maxFactor, float minFactor)
    {
        // TODO add fix so we cant generate same enum twice
        var randomType = Enum.GetValues(typeof(BootsAffixType)).Cast<BootsAffixType>().OrderBy(_ => random.Next()).FirstOrDefault();
        float value = 0.0f;
        float max = 1.0f;
        float min = 0.0f;
        switch(randomType)
        {
        case BootsAffixType.ColdResitance:
        case BootsAffixType.FireResistance:
        case BootsAffixType.PoisonResistance:
        case BootsAffixType.LightningResistance:
            max = Constants.Generate.Boots.MaxSingleResistance;
            min = Constants.Generate.Boots.MinSingleResistance;
        break;
        case BootsAffixType.AllResistance:
            max = Constants.Generate.Boots.MaxAllResistance;
            min = Constants.Generate.Boots.MinAllResistance;
        break;
        case BootsAffixType.BlockChance:
            max = Constants.Generate.Boots.MaxBlockChance;
            min = Constants.Generate.Boots.MinBlockChance;
        break;
        case BootsAffixType.MobilitySkillCooldownReduction:
            max = Constants.Generate.Boots.MaxMobilitySkillCooldownReduction;
            min = Constants.Generate.Boots.MinMobilitySkillCooldownReduction;
        break;
        case BootsAffixType.Agility:
            max = Constants.Generate.Boots.MaxAgility;
            min = Constants.Generate.Boots.MinAgility;
        break;
        case BootsAffixType.ResourceCostReduction:
            max = Constants.Generate.Boots.MaxResourceCostReduction;
            min = Constants.Generate.Boots.MinResourceCostReduction;
        break;
        case BootsAffixType.DodgeChance:
            max = Constants.Generate.Boots.MaxDodgeChance;
            min = Constants.Generate.Boots.MinDodgeChance;
        break;
        default:
            value = 0.0f;
        break;

        }

        max = Mathf.Lerp(min, max, maxFactor);
        min = Mathf.Lerp(min, max, minFactor);
        value = UnityEngine.Random.Range(min, max);

        return new BootsAffix{Type = randomType, Value = value};
    }
}

public struct MagicBoots : IComponentData
{
    public float MovementSpeed;
    public int Armor;

    public BootsAffix Affix;
}

public struct RareBoots : IComponentData
{
    public float MovementSpeed;
    public int Armor;  
    public BootsAffix Affix0;
    public BootsAffix Affix1;
    public BootsAffix Affix2;  
}




