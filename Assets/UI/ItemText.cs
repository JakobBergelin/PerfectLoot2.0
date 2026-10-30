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

    /// <summary>One line of an item's stats: rich text, and the affix it's about (for its icon; null for other lines).</summary>
    public struct StatLine
    {
        public string Text;
        public Affix? Affix;
        public bool Wide;       // Not part of the stat column (the power, notes): no icon space
    }

    /// <summary>The item's stats as text (Lines joined), for places without icons.</summary>
    public static string Body(Item item, Item compareTo = null) => string.Join("\n", Lines(item, compareTo).Select(line => line.Text));

    /// <summary>
    /// The item's stats, a line each. With compareTo, each affix gets its difference to compareTo's total of that affix
    /// (green better, red worse), affixes compareTo lacks are marked new, and affixes this item lacks are listed as missing.
    /// </summary>
    public static List<StatLine> Lines(Item item, Item compareTo = null)
    {
        var lines = new List<StatLine>();
        var settings = LootGenerationManager.Instance != null ? LootGenerationManager.Instance.GenerationSettings : null;
        var compared = new HashSet<Affix>();

        // Always the same order (AffixOrder): the slot's intrinsic stats, its fixed values (muted, e.g. a weapon's
        // cooldown and reach), the base affixes, then the rolled affixes
        var intrinsic = new HashSet<Affix>(item.BaseItem.AllRanges.Where(r => r.Source == AffixSource.Intrinsic).Select(r => r.Affix));
        var baseAffixes = item.BaseAffixes ?? new List<Rolled>();
        AddAffixes(lines, item, Sorted(baseAffixes.Where(rolled => intrinsic.Contains(rolled.Affix))), isBaseAffix: true, settings, compareTo, compared);
        foreach (var line in item.BaseItem.FixedStatLines)
            lines.Add(new StatLine { Text = $"<color={RangeColor}>{line}</color>" });
        AddAffixes(lines, item, Sorted(baseAffixes.Where(rolled => !intrinsic.Contains(rolled.Affix))), isBaseAffix: true, settings, compareTo, compared);
        AddAffixes(lines, item, Sorted(item.RollableAffixes), isBaseAffix: false, settings, compareTo, compared);

        if (compareTo != null)
        {
            foreach (var rolled in Sorted(AllAffixes(compareTo).Where(rolled => Total(item, rolled.Affix) == null)))
                lines.Add(new StatLine { Text = $"<color={WorseColor}>Missing: +{Format(rolled.Affix, rolled.Value)} {AffixName(rolled.Affix)}</color>", Affix = rolled.Affix });
        }

        // Legendary/Unique power: name in the item's rarity color, then its description with the numbers filled in
        if (item.BaseItem is ILegendaryItem legendary && legendary.Power != null)
        {
            var color = ColorUtility.ToHtmlStringRGB(TitleColor(item));
            if (lines.Count > 0) lines.Add(new StatLine { Text = "", Wide = true });
            lines.Add(new StatLine { Text = $"<b><color=#{color}>{legendary.Power.DisplayName}</color></b>", Wide = true });
            lines.Add(new StatLine { Text = legendary.Power.FormattedDescription?.TrimEnd() ?? "", Wide = true });
        }

        // Failed a check when loaded (ItemValidation): kept, but it won't count for leaderboards
        if (item.Unverified)
        {
            if (lines.Count > 0) lines.Add(new StatLine { Text = "", Wide = true });
            lines.Add(new StatLine { Text = $"<color={WorseColor}>Unverified</color>", Wide = true });
        }
        if (lines.Count == 0) lines.Add(new StatLine { Text = "No stats", Wide = true });
        return lines;
    }

    // "+177 Strength  [150 – 180]": the range this affix could roll on this item at its rarity, muted
    static void AddAffixes(List<StatLine> lines, Item item, IEnumerable<Rolled> affixes, bool isBaseAffix, GenerationSettings settings,
                           Item compareTo, HashSet<Affix> compared)
    {
        foreach (var rolled in affixes)
        {
            var sb = new StringBuilder($"+{Format(rolled.Affix, rolled.Value)} {AffixName(rolled.Affix)}");
            if (item.BaseItem.TryGetRollRange(rolled.Affix, isBaseAffix, item.Rarity, settings, out float min, out float max))
                sb.Append($"  <color={RangeColor}>[{Format(rolled.Affix, min)} – {Format(rolled.Affix, max)}]</color>");
            // The difference goes on the affix's first line; totals cover affixes that appear more than once
            if (compareTo != null && compared.Add(rolled.Affix))
                sb.Append("  " + Difference(rolled.Affix, Total(item, rolled.Affix) ?? 0f, Total(compareTo, rolled.Affix)));
            lines.Add(new StatLine { Text = sb.ToString(), Affix = rolled.Affix });
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

    // Stable, so an affix rolled twice keeps its two lines in roll order
    static IEnumerable<Rolled> Sorted(IEnumerable<Rolled> affixes) =>
        (affixes ?? Enumerable.Empty<Rolled>()).OrderBy(rolled => AffixOrder.Index(rolled.Affix));

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
        Affix.CriticalChance or Affix.CriticalDamage or Affix.ResourceSteal or Affix.BuffDuration or Affix.ProjectileSpeed or
        Affix.ElementalDamage or Affix.FireDamage or Affix.ColdDamage or Affix.LightningDamage or Affix.PoisonDamage => true,
        _ => false
    };

    public static string Format(Affix affix, float value) =>
        IsPercentAffix(affix) ? (value * 100f).ToString("0.#") + "%" : value.ToString("0.#");

    public static string AffixName(Affix affix) => SplitWords(affix.ToString());

    public static string SplitWords(string name) => Regex.Replace(name, "(\\B[A-Z])", " $1");
}
