using System;
using Unity.Entities;
using UnityEngine;

/// <summary>Tuning of the status effects that damage types apply (bleed, burn, poison, chill, freeze, charge).</summary>
[CreateAssetMenu(fileName = "StatusEffectSettings", menuName = "Settings/StatusEffectSettings")]
public class StatusEffectSettings : ScriptableObject
{
    public StatusEffectConfig Values = new StatusEffectConfig
    {
        BleedDuration = 1.0f,
        BleedDamageHealthMultiplier = 0.05f,
        BurnDuration = 3.0f,
        PoisonDuration = 5.0f,
        PoisonDamagePerSecond = 3.0f,
        ChilledDamageMultiplier = 2.0f,
        ChillDuration = 4.0f,
        ChillMaxStacks = 100,
        ChillSlowPerStack = 0.005f,
        FrozenDuration = 2.0f,
        ChargedDamageMultiplier = 0.3f,
        ChargedRadius = 4.0f,
    };
}

/// <summary>Baked from StatusEffectSettings by GameSettingsAuthoring; read as a singleton.</summary>
[Serializable]
public struct StatusEffectConfig : IComponentData
{
    [Header("Bleed (physical)")]
    [Tooltip("Seconds each bleed lasts (every physical hit adds one)")]
    public float BleedDuration;
    [Tooltip("Fraction of current health dealt per second, per bleed")]
    public float BleedDamageHealthMultiplier;

    [Header("Burn (fire)")]
    [Tooltip("Seconds each burn lasts; the hit's damage is dealt again over this time")]
    public float BurnDuration;

    [Header("Poison")]
    [Tooltip("Seconds poison lasts; refreshed on every poison hit")]
    public float PoisonDuration;
    public float PoisonDamagePerSecond;

    [Header("Chill / Freeze (cold)")]
    [Tooltip("Chill stacks per point of cold damage")]
    public float ChilledDamageMultiplier;
    [Tooltip("Seconds chill lasts after the last cold hit")]
    public float ChillDuration;
    [Tooltip("At this many stacks the unit freezes")]
    public int ChillMaxStacks;
    [Tooltip("Movement slow per chill stack (0.005 = 0.5%)")]
    public float ChillSlowPerStack;
    [Tooltip("Seconds a freeze lasts")]
    public float FrozenDuration;

    [Header("Charged (lightning)")]
    [Tooltip("Fraction of the hit's damage dealt by the charge")]
    public float ChargedDamageMultiplier;
    [Tooltip("Radius the charge jumps in")]
    public float ChargedRadius;
}
