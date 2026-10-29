using System.Text.RegularExpressions;
using Equipment;

/// <summary>Formatting of affixes and values in the Item Editor (percent affixes are stored as fractions).</summary>
public static class ItemEditorFormat
{
    public static bool IsPercent(Affix affix) => affix switch
    {
        Affix.MovementSpeed or Affix.BlockChance or Affix.CooldownReduction or Affix.ResourceCostReduction or
        Affix.DodgeChance or Affix.AttackSpeed or Affix.Lifesteal or Affix.Damage or Affix.AreaOfEffect or
        Affix.CriticalChance or Affix.CriticalDamage or Affix.ResourceSteal or Affix.BuffDuration or Affix.ProjectileSpeed => true,
        _ => false
    };

    public static string Value(Affix affix, float value) =>
        IsPercent(affix) ? (value * 100f).ToString("0.#") + "%" : value.ToString("0.##");

    public static string Range(Affix affix, RollRange range) =>
        range == null ? "" : $"{Value(affix, range.Min)}–{Value(affix, range.Max)}";

    public static string AffixName(Affix affix) => Regex.Replace(affix.ToString(), "(\\B[A-Z])", " $1");

    public const string DpsTooltip = "Weapon Damage ÷ Weapon Cooldown: damage per second with a skill that attacks once a second, " +
                                     "at base attack speed. Real DPS also depends on the skill and attack speed; the ± damage range averages out.";

    /// <summary>A weapon's damage per second for a 1-second skill (a cooldown of 0 counts as 1, like in game).</summary>
    public static (float Min, float Max) Dps(float damageMin, float damageMax, float cooldown)
    {
        if (cooldown <= 0f) cooldown = 1f;
        return (damageMin / cooldown, damageMax / cooldown);
    }

    public static string Dps(RollableWeaponItem weapon)
    {
        if (weapon.WeaponDamage == null) return "";
        var (min, max) = Dps(weapon.WeaponDamage.Min, weapon.WeaponDamage.Max, weapon.WeaponCooldown);
        return $"{min:0.##}–{max:0.##}";
    }
}
