using System;
using System.Collections.Generic;
using System.Text;

namespace Equipment
{
    /// <summary>Raw per-affix totals across a set of items. Serializable so it shows in the Inspector.</summary>
    [Serializable]
    public class StatSummary
    {
        public List<Rolled> Totals = new List<Rolled>();

        public float Get(Affix affix)
        {
            foreach (var total in Totals)
                if (total.Affix == affix) return total.Value;
            return 0f;
        }

        public void Clear() => Totals.Clear();

        public void Add(Affix affix, float value)
        {
            foreach (var total in Totals)
            {
                if (total.Affix != affix) continue;
                total.Value += value;
                return;
            }
            Totals.Add(new Rolled { Affix = affix, Value = value });
        }

        public void AddItem(Item item)
        {
            if (item == null) return;
            if (item.BaseAffixes != null)
                foreach (var rolled in item.BaseAffixes) Add(rolled.Affix, rolled.Value);
            if (item.RollableAffixes != null)
                foreach (var rolled in item.RollableAffixes) Add(rolled.Affix, rolled.Value);
        }

        /// <summary>Orders the totals by Affix so the Inspector list is stable.</summary>
        public void Sort() => Totals.Sort((a, b) => a.Affix.CompareTo(b.Affix));

        public override string ToString()
        {
            if (Totals.Count == 0) return "(no stats)";
            var sb = new StringBuilder();
            foreach (var total in Totals)
                sb.AppendLine($"{total.Affix}: {total.Value:0.###}");
            return sb.ToString();
        }
    }
}
