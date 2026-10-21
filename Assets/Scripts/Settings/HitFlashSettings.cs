using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[CreateAssetMenu(fileName = "HitFlashSettings", menuName = "Settings/HitFlashSettings")]
public class HitFlashSettings : ScriptableObject
{
    [Tooltip("Seconds a unit flashes after taking a hit")]
    public float Duration = 0.12f;
    [Tooltip("How far toward the flash color the unit goes at the start of the flash (0-1)")]
    [Range(0f, 1f)] public float Strength = 0.8f;
    [Tooltip("How much of the damage type's color is mixed into the white flash (0 = pure white)")]
    [Range(0f, 1f)] public float TypeTint = 0.35f;

    [Header("Damage type colors")]
    public Color Physical = Color.white;
    public Color Fire = new Color(1f, 0.45f, 0.1f);
    public Color Cold = new Color(0.45f, 0.8f, 1f);
    public Color Lightning = new Color(1f, 0.95f, 0.3f);
    public Color Poison = new Color(0.35f, 1f, 0.3f);

    /// <summary>The values as a component. Colors are converted to linear, as the shader expects.</summary>
    public HitFlashConfig ToConfig() => new HitFlashConfig
    {
        Duration = Duration,
        Strength = Strength,
        Physical = FlashColor(Physical),
        Fire = FlashColor(Fire),
        Cold = FlashColor(Cold),
        Lightning = FlashColor(Lightning),
        Poison = FlashColor(Poison),
    };

    // White with TypeTint of the type's color mixed in
    float4 FlashColor(Color typeColor)
    {
        var color = Color.Lerp(Color.white, typeColor, TypeTint).linear;
        return new float4(color.r, color.g, color.b, 1f);
    }
}

/// <summary>Baked from HitFlashSettings by GameSettingsAuthoring; read as a singleton.</summary>
public struct HitFlashConfig : IComponentData
{
    public float Duration;
    public float Strength;
    public float4 Physical;
    public float4 Fire;
    public float4 Cold;
    public float4 Lightning;
    public float4 Poison;

    public float4 FlashColor(DamageType type) => type switch
    {
        DamageType.Fire => Fire,
        DamageType.Cold => Cold,
        DamageType.Lightning => Lightning,
        DamageType.Poison => Poison,
        _ => Physical,
    };
}
