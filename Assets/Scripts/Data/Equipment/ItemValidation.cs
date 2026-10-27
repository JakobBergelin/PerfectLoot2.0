using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Equipment
{
    /// <summary>
    /// Checks that a rolled item could have come from the game: plausible for its base item (rarity, affixes, and
    /// values within the ranges of its rarity) and exactly what its seed rolls. Runs on load for now; meant to run on a
    /// server later, which also knows which Guids and seeds it handed out (a local check can't stop a picked seed).
    /// </summary>
    public static class ItemValidation
    {
        // Values rolled on another machine or runtime may differ in the last bits
        const float Tolerance = 1e-4f;

        /// <summary>What's wrong with the item, or an empty list when it's valid.</summary>
        public static List<string> Check(Item item, GenerationSettings settings)
        {
            var problems = new List<string>();
            if (item == null || item.BaseItem == null)
            {
                problems.Add("no base item");
                return problems;
            }
            if (string.IsNullOrEmpty(item.Guid)) problems.Add("no Guid");
            if (!item.BaseItem.PossibleRarities.Contains(item.Rarity)) problems.Add($"{item.BaseItem.Name} can't be {item.Rarity}");
            if (settings == null)
            {
                problems.Add("no GenerationSettings to check the rolls against");
                return problems;
            }

            CheckBaseAffixes(item, settings, problems);
            CheckRollableAffixes(item, settings, problems);
            CheckSeed(item, settings, problems);
            return problems;
        }

        public static bool IsValid(Item item, GenerationSettings settings) => Check(item, settings).Count == 0;

        // Exactly the intrinsic stats and BaseAffixes, in that order, each within its range at the item's rarity
        static void CheckBaseAffixes(Item item, GenerationSettings settings, List<string> problems)
        {
            var expected = item.BaseItem.AllRanges.Where(range => range.Source != AffixSource.Rollable).ToList();
            var actual = item.BaseAffixes ?? new List<Rolled>();
            if (actual.Count != expected.Count)
                problems.Add($"{actual.Count} base affixes, expected {expected.Count}");

            var (minLerp, maxLerp) = settings.GetBaseRange(item.Rarity);
            for (int i = 0; i < Mathf.Min(actual.Count, expected.Count); i++)
            {
                if (actual[i] == null || actual[i].Affix != expected[i].Affix)
                    problems.Add($"base affix {i + 1} is {actual[i]?.Affix}, expected {expected[i].Affix}");
                else if (!InWindow(actual[i].Value, expected[i].Range, minLerp, maxLerp))
                    problems.Add($"{actual[i].Affix} {actual[i].Value} is outside its range");
            }
        }

        // The rarity's number of RollableAffixes, never one entry twice, each within its range at the item's rarity
        static void CheckRollableAffixes(Item item, GenerationSettings settings, List<string> problems)
        {
            var actual = item.RollableAffixes ?? new List<Rolled>();
            int expectedCount = item.BaseItem.RollableAffixCount(item.Rarity);
            if (actual.Count != expectedCount)
                problems.Add($"{actual.Count} rolled affixes, expected {expectedCount} for {item.Rarity}");

            var (minLerp, maxLerp) = settings.GetAffixRange(item.Rarity);
            var entries = item.BaseItem.AllRanges.Where(range => range.Source == AffixSource.Rollable).ToList();
            foreach (var group in actual.Where(rolled => rolled != null).GroupBy(rolled => rolled.Affix))
            {
                var ranges = entries.Where(entry => entry.Affix == group.Key).Select(entry => entry.Range).ToList();
                if (ranges.Count == 0)
                {
                    problems.Add($"{item.BaseItem.Name} can't roll {group.Key}");
                    continue;
                }
                if (group.Count() > ranges.Count) problems.Add($"{group.Key} rolled {group.Count()} times");
                foreach (var rolled in group)
                    if (!ranges.Any(range => InWindow(rolled.Value, range, minLerp, maxLerp)))
                        problems.Add($"{rolled.Affix} {rolled.Value} is outside its range");
            }
        }

        // Rolling the seed again gives this exact item
        static void CheckSeed(Item item, GenerationSettings settings, List<string> problems)
        {
            if (item.RollVersion != RollableItem.CurrentRollVersion)
            {
                problems.Add($"roll version {item.RollVersion} can't be checked (current is {RollableItem.CurrentRollVersion})");
                return;
            }
            var rerolled = item.BaseItem.RollFromSeed(item.Seed, settings);
            if (rerolled.Rarity != item.Rarity)
                problems.Add($"its seed rolls {rerolled.Rarity}, not {item.Rarity}");
            else if (!SameAffixes(rerolled.BaseAffixes, item.BaseAffixes) || !SameAffixes(rerolled.RollableAffixes, item.RollableAffixes))
                problems.Add("its affixes aren't what its seed rolls");
        }

        static bool SameAffixes(List<Rolled> expected, List<Rolled> actual)
        {
            actual ??= new List<Rolled>();
            if (expected.Count != actual.Count) return false;
            for (int i = 0; i < expected.Count; i++)
                if (actual[i] == null || actual[i].Affix != expected[i].Affix || !Close(actual[i].Value, expected[i].Value)) return false;
            return true;
        }

        static bool InWindow(float value, RollRange range, float minLerp, float maxLerp)
        {
            float a = Mathf.Lerp(range.Min, range.Max, minLerp);
            float b = Mathf.Lerp(range.Min, range.Max, maxLerp);
            float slack = Tolerance * Mathf.Max(1f, Mathf.Abs(a), Mathf.Abs(b));
            return value >= Mathf.Min(a, b) - slack && value <= Mathf.Max(a, b) + slack;
        }

        static bool Close(float a, float b) => Mathf.Abs(a - b) <= Tolerance * Mathf.Max(1f, Mathf.Abs(a), Mathf.Abs(b));
    }
}
