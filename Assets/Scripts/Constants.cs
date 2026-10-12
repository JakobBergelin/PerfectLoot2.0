
using System;
using System.Collections.Generic;

public static class Constants
{
    public readonly static float Range = 15.0f;
    public readonly static float MovementSpeed = 4.0f;

    public readonly static float DefaultBurnTime = 3.0f;
    public readonly static float DefaultBleedTime = 1.0f;

    public readonly static float DefaultBleedDamageHealthMultiplier = 0.05f;

    public readonly static float DefaultChargedDamageMultiplier = 0.3f;
    public readonly static float DefaultChargedRadius = 4.0f;
    public readonly static float DefaultPoisonTime = 5.0f;
    public readonly static float DefaultFrozenTime = 2.0f;
    public readonly static float DefaultChilledDamageMultiplier = 2.0f;

    public readonly static float DefaultPoisonDamagePerSecond = 3.0f;

    public readonly static float RotationSpeed = 360f;

    public readonly static float BlockAmount = 5.0f;

    public readonly static float ReductionPerArmor = 0.04f;

    public static class Generate
    {        
        public static class Boots
        {
            public readonly static int MaxArmor = 5;
            public readonly static int MinArmor = 1;
            public readonly static float MaxMovementSpeed = 0.4f;
            public readonly static float MinMovementSpeed = 0.1f;

            public readonly static float MaxSingleResistance = 0.4f;
            public readonly static float MinSingleResistance = 0.1f;
            public readonly static float MaxAllResistance = 0.15f;
            public readonly static float MinAllResistance = 0.05f;

            public readonly static float MaxBlockChance = 0.05f;
            public readonly static float MinBlockChance = 0.02f;
            internal static readonly float MaxMobilitySkillCooldownReduction = 0.12f;
            internal static readonly float MinMobilitySkillCooldownReduction = 0.05f;
            internal static readonly float MaxAgility = 5f;
            internal static readonly float MinAgility = 1f;
            internal static readonly float MaxResourceCostReduction = 0.1f;
            internal static readonly float MinResourceCostReduction = 0.03f;
            internal static readonly float MaxDodgeChance = 0.1f;
            internal static readonly float MinDodgeChance = 0.03f;
        }

        public static class Sword
        {
            public readonly static float MaxDamage = 5.0f;
            public readonly static float MinDamage = 2.0f;
        }

        public static class Crossbow
        {
            public readonly static float MaxDamage = 6.0f;
            public readonly static float MinDamage = 3.0f;
        }

        public static class Dagger
        {
            public readonly static float MaxDamage = 3f;
            public readonly static float MinDamage = 2f;
            public readonly static float MaxCrit = 0.3f;
            public readonly static float MinCrit = 0.05f;
        }
    }

}