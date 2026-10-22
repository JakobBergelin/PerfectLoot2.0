using System.Collections.Generic;
using System.Linq;
using Equipment;
using UnityEngine;

/// <summary>
/// Finds affix ranges that stand out from other items of the same slot. For every slot + affix it takes the range
/// midpoints (Min+Max)/2 of all items, and scores each one robustly: (midpoint - median) / spread, where spread is
/// the median absolute deviation. |score| >= 1 is a mild outlier, >= 2 a strong one. Range width is compared the same
/// way but only mentioned in the tooltip.
/// </summary>
public class ItemOutliers
{
    public const float Mild = 1f;
    public const float Strong = 2f;
    const int MinimumGroupSize = 3;

    public struct Verdict
    {
        public bool HasData;
        public float Score;         // Signed: positive = higher than typical
        public string Tooltip;
        public bool IsOutlier => HasData && Mathf.Abs(Score) >= Mild;
    }

    struct Group
    {
        public int Count;
        public float Median, Spread, Min, Max;
        public float WidthMedian, WidthSpread;
    }

    readonly Dictionary<(ItemType, Affix), Group> _groups = new Dictionary<(ItemType, Affix), Group>();

    public void Rebuild(IEnumerable<RollableItem> items)
    {
        _groups.Clear();
        var midpoints = new Dictionary<(ItemType, Affix), List<float>>();
        var widths = new Dictionary<(ItemType, Affix), List<float>>();
        foreach (var item in items)
        {
            if (item == null) continue;
            foreach (var (affix, range, _) in item.AllRanges)
            {
                var key = (item.ItemType, affix);
                if (!midpoints.TryGetValue(key, out var list)) midpoints[key] = list = new List<float>();
                if (!widths.TryGetValue(key, out var widthList)) widths[key] = widthList = new List<float>();
                list.Add(Midpoint(range));
                widthList.Add(Width(range));
            }
        }

        foreach (var pair in midpoints)
        {
            var values = pair.Value;
            float median = Median(values);
            float widthMedian = Median(widths[pair.Key]);
            _groups[pair.Key] = new Group
            {
                Count = values.Count,
                Median = median,
                Spread = Spread(values, median),
                Min = values.Min(),
                Max = values.Max(),
                WidthMedian = widthMedian,
                WidthSpread = Spread(widths[pair.Key], widthMedian),
            };
        }
    }

    public Verdict Evaluate(RollableItem item, Affix affix, RollRange range)
    {
        if (item == null || range == null || !_groups.TryGetValue((item.ItemType, affix), out var group))
            return new Verdict { Tooltip = "No data" };

        string name = ItemEditorFormat.AffixName(affix);
        if (group.Count < MinimumGroupSize)
            return new Verdict { Tooltip = $"{name} on {item.ItemType}: only {group.Count} item(s) have it, not enough to compare." };

        float midpoint = Midpoint(range);
        float score = (midpoint - group.Median) / group.Spread;
        float widthScore = (Width(range) - group.WidthMedian) / group.WidthSpread;

        string verdict = score >= Strong ? "much higher than typical"
            : score >= Mild ? "higher than typical"
            : score <= -Strong ? "much lower than typical"
            : score <= -Mild ? "lower than typical"
            : "typical";
        string tooltip =
            $"{name} on {item.ItemType} ({group.Count} items): typical midpoint {ItemEditorFormat.Value(affix, group.Median)}, " +
            $"range of midpoints {ItemEditorFormat.Value(affix, group.Min)} – {ItemEditorFormat.Value(affix, group.Max)}.\n" +
            $"This: {ItemEditorFormat.Range(affix, range)} (midpoint {ItemEditorFormat.Value(affix, midpoint)}) → {verdict}.";
        if (widthScore >= Strong) tooltip += "\nThe range is unusually wide.";
        else if (widthScore <= -Strong) tooltip += "\nThe range is unusually narrow.";

        return new Verdict { HasData = true, Score = score, Tooltip = tooltip };
    }

    /// <summary>Number of mild-or-stronger outliers above and below typical on an item.</summary>
    public (int High, int Low) Count(RollableItem item)
    {
        int high = 0, low = 0;
        if (item == null) return (0, 0);
        foreach (var (affix, range, _) in item.AllRanges)
        {
            var verdict = Evaluate(item, affix, range);
            if (!verdict.IsOutlier) continue;
            if (verdict.Score > 0) high++; else low++;
        }
        return (high, low);
    }

    /// <summary>Heat color for a score: clear when typical, warmer above typical, cooler below.</summary>
    public static Color ScoreColor(Verdict verdict)
    {
        if (!verdict.IsOutlier) return Color.clear;
        bool strong = Mathf.Abs(verdict.Score) >= Strong;
        return verdict.Score > 0
            ? (strong ? new Color(0.95f, 0.35f, 0.15f, 0.75f) : new Color(1f, 0.65f, 0.2f, 0.45f))
            : (strong ? new Color(0.2f, 0.45f, 1f, 0.75f) : new Color(0.4f, 0.7f, 1f, 0.45f));
    }

    public static string ScoreArrow(Verdict verdict)
    {
        if (!verdict.IsOutlier) return "";
        bool strong = Mathf.Abs(verdict.Score) >= Strong;
        return verdict.Score > 0 ? (strong ? "▲▲" : "▲") : (strong ? "▼▼" : "▼");
    }

    static float Midpoint(RollRange range) => (range.Min + range.Max) * 0.5f;
    static float Width(RollRange range) => Mathf.Abs(range.Max - range.Min);

    static float Median(List<float> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int n = sorted.Count;
        return n == 0 ? 0f : n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) * 0.5f;
    }

    // Median absolute deviation, scaled to be comparable to a standard deviation. Never 0, so a group of equal
    // values still flags the one item that differs.
    static float Spread(List<float> values, float median)
    {
        float mad = Median(values.Select(v => Mathf.Abs(v - median)).ToList()) * 1.4826f;
        return Mathf.Max(mad, Mathf.Abs(median) * 0.05f, 1e-4f);
    }
}
