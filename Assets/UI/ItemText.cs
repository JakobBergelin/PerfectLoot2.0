using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Equipment;
using UnityEngine;

/// <summary>
/// Rich text for rolled items, shared by the character panel tooltip, the item database panel and the drop popup:
/// fixed stats, affixes with the range they can roll at the item's rarity, and the Legendary/Unique power.
/// </summary>
public static class ItemText
{
    public const string RangeColor = "#8A8F99";
    const string BetterColor = "#4CC94C";
    const string WorseColor = "#E05050";

    public static Color TitleColor(Item item) => RarityColors.Get(item.Rarity) ?? Color.white;

    public static string Subtitle(Item item) => $"{item.Rarity} {item.ItemType}";

    /// <summary>
    /// The item's stats. With compareTo, each affix gets its difference to compareTo's total of that affix (green
    /// better, red worse), affixes compareTo lacks are marked new, and affixes this item lacks are listed as missing.
    /// </summary>
    public static string Body(Item item, Item compareTo = null)
    {
        var sb = new StringBuilder();
        // Fixed (not rolled) stats first, muted, e.g. a weapon's cooldown and reach
        foreach (var line in item.BaseItem.FixedStatLines)
            sb.AppendLine($"<color={RangeColor}>{line}</color>");

        var settings = LootGenerationManager.Instance != null ? LootGenerationManager.Instance.GenerationSettings : null;
        var compared = new HashSet<Affix>();
        AppendAffixes(sb, item, item.BaseAffixes, isBaseAffix: true, settings, compareTo, compared);
        AppendAffixes(sb, item, item.RollableAffixes, isBaseAffix: false, settings, compareTo, compared);

        if (compareTo != null)
        {
            foreach (var rolled in AllAffixes(compareTo).Where(rolled => Total(item, rolled.Affix) == null))
                sb.AppendLine($"<color={WorseColor}>Missing: +{Format(rolled.Affix, rolled.Value)} {AffixName(rolled.Affix)}</color>");
        }

        // Legendary/Unique power: name in the item's rarity color, then its description with the numbers filled in
        if (item.BaseItem is ILegendaryItem legendary && legendary.Power != null)
        {
            var color = ColorUtility.ToHtmlStringRGB(TitleColor(item));
            sb.AppendLine();
            sb.AppendLine($"<b><color=#{color}>{legendary.Power.DisplayName}</color></b>");
            sb.AppendLine(legendary.Power.FormattedDescription);
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "No stats";
    }

    // "+177 Strength  [150 – 180]": the range this affix could roll on this item at its rarity, muted
    static void AppendAffixes(StringBuilder sb, Item item, List<Rolled> affixes, bool isBaseAffix, GenerationSettings settings,
                              Item compareTo, HashSet<Affix> compared)
    {
        if (affixes == null) return;
        foreach (var rolled in affixes)
        {
            sb.Append($"+{Format(rolled.Affix, rolled.Value)} {AffixName(rolled.Affix)}");
            if (item.BaseItem.TryGetRollRange(rolled.Affix, isBaseAffix, item.Rarity, settings, out float min, out float max))
                sb.Append($"  <color={RangeColor}>[{Format(rolled.Affix, min)} – {Format(rolled.Affix, max)}]</color>");
            // The difference goes on the affix's first line; totals cover affixes that appear more than once
            if (compareTo != null && compared.Add(rolled.Affix))
                sb.Append("  " + Difference(rolled.Affix, Total(item, rolled.Affix) ?? 0f, Total(compareTo, rolled.Affix)));
            sb.AppendLine();
        }
    }

    static string Difference(Affix affix, float value, float? other)
    {
        if (other == null) return $"<color={BetterColor}>(new)</color>";
        float delta = value - other.Value;
        if (Mathf.Approximately(delta, 0f)) return $"<color={RangeColor}>(=)</color>";
        return delta > 0f
            ? $"<color={BetterColor}>(+{Format(affix, delta)})</color>"
            : $"<color={WorseColor}>(-{Format(affix, -delta)})</color>";
    }

    static IEnumerable<Rolled> AllAffixes(Item item) =>
        (item.BaseAffixes ?? Enumerable.Empty<Rolled>()).Concat(item.RollableAffixes ?? Enumerable.Empty<Rolled>());

    /// <summary>The summed value of an affix on an item, or null when the item doesn't have it.</summary>
    static float? Total(Item item, Affix affix)
    {
        float? total = null;
        foreach (var rolled in AllAffixes(item))
            if (rolled.Affix == affix) total = (total ?? 0f) + rolled.Value;
        return total;
    }

    public static bool HasAffix(Item item, Affix affix) => AllAffixes(item).Any(rolled => rolled.Affix == affix);

    // Affixes rolled as fractions (0.1 = 10%); the rest are flat values
    public static bool IsPercentAffix(Affix affix) => affix switch
    {
        Affix.MovementSpeed or Affix.BlockChance or Affix.CooldownReduction or Affix.ResourceCostReduction or
        Affix.DodgeChance or Affix.AttackSpeed or Affix.Lifesteal or Affix.Damage or Affix.AreaOfEffect or
        Affix.CriticalChance or Affix.CriticalDamage or Affix.ResourceSteal => true,
        _ => false
    };

    public static string Format(Affix affix, float value) =>
        IsPercentAffix(affix) ? (value * 100f).ToString("0.#") + "%" : value.ToString("0.#");

    public static string AffixName(Affix affix) => SplitWords(affix.ToString());

    public static string SplitWords(string name) => Regex.Replace(name, "(\\B[A-Z])", " $1");
}
