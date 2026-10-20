using System;
using Unity.Entities;
using UnityEngine;

[CreateAssetMenu(fileName = "CombatSettings", menuName = "Settings/CombatSettings")]
public class CombatSettings : ScriptableObject
{
    public CombatConfig Values = new CombatConfig
    {
        BlockAmount = 5.0f,
        ReductionPerArmor = 0.04f,
        UnarmedWeaponReach = 1.1f,
        HomingTargetRange = 15.0f,
    };
}

/// <summary>Baked from CombatSettings by GameSettingsAuthoring; read as a singleton.</summary>
[Serializable]
public struct CombatConfig : IComponentData
{
    [Tooltip("Damage removed from a hit when it is blocked")]
    public float BlockAmount;
    [Tooltip("Physical damage reduction per point of armor (compounding)")]
    public float ReductionPerArmor;
    [Tooltip("Weapon reach used when no weapon is equipped")]
    public float UnarmedWeaponReach;
    [Tooltip("Radius a homing projectile searches for a target in")]
    public float HomingTargetRange;
}
