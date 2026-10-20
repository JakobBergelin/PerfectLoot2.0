using System;
using Unity.Entities;
using UnityEngine;

[CreateAssetMenu(fileName = "StatusEffectSettings", menuName = "Settings/StatusEffectSettings")]
public class StatusEffectSettings : ScriptableObject
{
    public StatusEffectConfig Values = new StatusEffectConfig
    {
        BleedTime = 1.0f,
        BleedDamageHealthMultiplier = 0.05f,
        BurnTime = 3.0f,
        ChilledDamageMultiplier = 2.0f,
        FrozenTime = 2.0f,
        ChargedDamageMultiplier = 0.3f,
        ChargedRadius = 4.0f,
        PoisonTime = 5.0f,
        PoisonDamagePerSecond = 3.0f,
    };
}

/// <summary>Baked from StatusEffectSettings by GameSettingsAuthoring; read as a singleton.</summary>
[Serializable]
public struct StatusEffectConfig : IComponentData
{
    [Header("Bleed (physical)")]
    [Tooltip("Seconds a bleed lasts")]
    public float BleedTime;
    [Tooltip("Fraction of current health dealt per second, per bleed")]
    public float BleedDamageHealthMultiplier;

    [Header("Burn (fire)")]
    [Tooltip("Seconds a burn lasts; the hit's damage is dealt again over this time")]
    public float BurnTime;

    [Header("Chill / Freeze (cold)")]
    [Tooltip("Chill built up per point of cold damage")]
    public float ChilledDamageMultiplier;
    [Tooltip("Seconds a freeze lasts")]
    public float FrozenTime;

    [Header("Charged (lightning)")]
    [Tooltip("Fraction of the hit's damage dealt by the charge")]
    public float ChargedDamageMultiplier;
    [Tooltip("Radius the charge jumps in")]
    public float ChargedRadius;

    [Header("Poison")]
    [Tooltip("Seconds poison lasts; refreshed on every poison hit")]
    public float PoisonTime;
    public float PoisonDamagePerSecond;
}
