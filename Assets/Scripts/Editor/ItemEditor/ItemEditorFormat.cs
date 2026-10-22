using System.Text.RegularExpressions;
using Equipment;

/// <summary>Formatting of affixes and values in the Item Editor (percent affixes are stored as fractions).</summary>
public static class ItemEditorFormat
{
    public static bool IsPercent(Affix affix) => affix switch
    {
        Affix.MovementSpeed or Affix.BlockChance or Affix.CooldownReduction or Affix.ResourceCostReduction or
        Affix.DodgeChance or Affix.AttackSpeed or Affix.Lifesteal or Affix.Damage or Affix.AreaOfEffect or
        Affix.CriticalChance or Affix.CriticalDamage or Affix.ResourceSteal => true,
        _ => false
    };

    public static string Value(Affix affix, float value) =>
        IsPercent(affix) ? (value * 100f).ToString("0.#") + "%" : value.ToString("0.##");

    public static string Range(Affix affix, RollRange range) =>
        range == null ? "" : $"{Value(affix, range.Min)}–{Value(affix, range.Max)}";

    public static string AffixName(Affix affix) => Regex.Replace(affix.ToString(), "(\\B[A-Z])", " $1");
}
