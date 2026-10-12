using Equipment;
using UnityEngine;

[CreateAssetMenu(fileName = "GenerationSettings", menuName = "Settings/GenerationSettings")]
public class GenerationSettings : ScriptableObject
{
    public float NormalBaseMax = 1f;
    public float NormalBaseMin = 0.0f;

    public float MagicBaseMax = 0.8f;
    public float MagicBaseMin = 0.1f;

    public float RareBaseMax = 0.6f;
    public float RareBaseMin = 0.2f;
    public float LegendaryBaseMax = 0.75f;
    public float LegendaryBaseMin = 0.4f;

    [Space]
    public float MagicAffixMax = 1.0f;
    public float MagicAffixMin = 0.0f;
    public float RareAffixMax = 0.8f;
    public float RareAffixMin = 0.2f;
    public float LegendaryAffixMax = 0.85f;
    public float LegendaryAffixMin = 0.5f;

    /// <summary>Roll window (0-1 lerp) for an item's base stats. Unique rolls the full range.</summary>
    public (float Min, float Max) GetBaseRange(Rarity rarity) => rarity switch
    {
        Rarity.Normal => (NormalBaseMin, NormalBaseMax),
        Rarity.Magic => (MagicBaseMin, MagicBaseMax),
        Rarity.Rare => (RareBaseMin, RareBaseMax),
        Rarity.Legendary => (LegendaryBaseMin, LegendaryBaseMax),
        _ => (0f, 1f)
    };

    /// <summary>Roll window (0-1 lerp) for rollable affixes. Unique rolls the full range.</summary>
    public (float Min, float Max) GetAffixRange(Rarity rarity) => rarity switch
    {
        Rarity.Magic => (MagicAffixMin, MagicAffixMax),
        Rarity.Rare => (RareAffixMin, RareAffixMax),
        Rarity.Legendary => (LegendaryAffixMin, LegendaryAffixMax),
        _ => (0f, 1f)
    };
}
