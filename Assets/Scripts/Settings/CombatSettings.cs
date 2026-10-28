using System;
using Unity.Entities;
using UnityEngine;

[CreateAssetMenu(fileName = "CombatSettings", menuName = "Settings/CombatSettings")]
public class CombatSettings : ScriptableObject
{
    public CombatConfig Values = new CombatConfig
    {
        BlockAmount = 5.0f,
        ArmorConstant = 100f,
        ElementalResistanceConstant = 20f,
        UnarmedWeaponReach = 1.1f,
        HomingTargetRange = 15.0f,
        KnockbackDamping = 8f,
        ProjectileMaxDistance = 50f,
        HomingAvoidLookahead = 3f,
        HomingAvoidStrength = 0.3f,
    };
}

/// <summary>Baked from CombatSettings by GameSettingsAuthoring; read as a singleton.</summary>
[Serializable]
public struct CombatConfig : IComponentData
{
    [Tooltip("Damage removed from a hit when it is blocked")]
    public float BlockAmount;
    [Tooltip("Armor needed for 50% physical damage mitigation. Mitigation = Armor / (Armor + ArmorConstant)")]
    public float ArmorConstant;
    [Tooltip("Resistance needed for 50% mitigation of cold, fire, lightning and poison damage. Mitigation = Resistance / (Resistance + ElementalResistanceConstant)")]
    public float ElementalResistanceConstant;
    [Tooltip("Weapon reach used when no weapon is equipped")]
    public float UnarmedWeaponReach;
    [Tooltip("Radius a homing projectile searches for a target in")]
    public float HomingTargetRange;
    [Tooltip("How fast knockback dies out, per second. A push travels about HitForce / (mass * KnockbackDamping) units.")]
    public float KnockbackDamping;
    [Tooltip("Projectiles farther than this from the player are removed")]
    public float ProjectileMaxDistance;
    [Tooltip("How far ahead (units) homing projectiles look for the ground or a wall to steer away from")]
    public float HomingAvoidLookahead;
    [Tooltip("How strongly homing projectiles steer away from the ground or walls ahead. Keep it weak: better hit a wall than miss the target")]
    public float HomingAvoidStrength;

    /// <summary>Fraction of damage mitigated by a defense value: value / (value + constant). 0 when the value is 0 or less.</summary>
    public static float Mitigation(float value, float constant) => value <= 0f ? 0f : value / (value + constant);

    /// <summary>
    /// Physical damage mitigated by armor: the attacker's pierce ignores that much armor (not below 0), then the multiplier
    /// (Golem) scales what's left, then Mitigation with ArmorConstant.
    /// </summary>
    public static float PhysicalResistance(int armor, float multiplier, float pierce, float armorConstant)
    {
        float effectiveArmor = Unity.Mathematics.math.max(0f, armor - Unity.Mathematics.math.max(0f, pierce)) * (1f + multiplier);
        return Mitigation(effectiveArmor, armorConstant);
    }
}
