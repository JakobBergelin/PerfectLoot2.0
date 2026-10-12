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

        /// <summary>Converts the totals into the ECS stat layout.</summary>
        public StatBlock ToStatBlock()
        {
            var block = new StatBlock();
            foreach (var total in Totals)
            {
                var v = total.Value;
                switch (total.Affix)
                {
                    case Affix.Strength: block.Strength += v; break;
                    case Affix.Agility: block.Agility += v; break;
                    case Affix.Intelligence: block.Intelligence += v; break;
                    case Affix.MovementSpeed: block.MovementSpeed += v; break;
                    case Affix.AllResistance: block.AllResistance += v; break;
                    case Affix.BlockChance: block.BlockChance += v; break;
                    case Affix.CooldownReduction: block.CooldownReduction += v; break;
                    case Affix.ResourceCostReduction: block.ResourceCostReduction += v; break;
                    case Affix.DodgeChance: block.DodgeChance += v; break;
                    case Affix.AttackSpeed: block.AttackSpeed += v; break;
                    case Affix.WeaponDamage: block.WeaponDamage += v; break;
                    case Affix.WeaponDamageRange: block.WeaponDamageRange += v; break;
                    case Affix.Lifesteal: block.Lifesteal += v; break;
                    case Affix.ProjectilePierces: block.Pierce += (int)Math.Round(v); break;
                    case Affix.ProjectileBounces: block.Bounces += (int)Math.Round(v); break;
                    case Affix.Damage: block.Damage += v; break;
                    case Affix.AreaOfEffect: block.AreaOfEffect += v; break;
                    case Affix.ColdResistance: block.ColdResistance += v; break;
                    case Affix.FireResistance: block.FireResistance += v; break;
                    case Affix.LightningResistance: block.LightningResistance += v; break;
                    case Affix.CriticalChance: block.CriticalChance += v; break;
                    case Affix.CriticalDamage: block.CriticalDamage += v; break;
                    case Affix.Armor: block.Armor += (int)Math.Round(v); break;
                    case Affix.ResourceSteal: block.ResourceSteal += v; break;
                    case Affix.Health: block.Health += v; break;
                    case Affix.HealthRegeneration: block.HealthRegeneration += v; break;
                    case Affix.Resource: block.Resource += v; break;
                    case Affix.ResourceRegeneration: block.ResourceRegeneration += v; break;
                }
            }
            return block;
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
