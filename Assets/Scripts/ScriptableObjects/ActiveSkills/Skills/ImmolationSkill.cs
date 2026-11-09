using Equipment;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// A toggle. While it's on it drains resource and enemies around the caster take fire damage that grows with
    /// Intelligence. Without enough resource it stays on but pauses until there's enough again.
    /// </summary>
    [CreateAssetMenu(fileName = "Immolation", menuName = "Active Skills/Immolation")]
    public class ImmolationSkill : ActiveSkill<Immolation>
    {
        [SerializeField] float resourceCostPerSecond = 2f;
        [Tooltip("Fire damage per second to each enemy in range, before Intelligence and the damage %")]
        public float DamagePerSecond = 2f;
        [Tooltip("Added to the damage per second for each point of Intelligence")]
        public float DamagePerIntelligence = 0.1f;
        [Tooltip("Meters around you, scaled by area of effect")]
        public float Radius = 4f;
        [Tooltip("Seconds between damage ticks")]
        [Min(0.05f)] public float TickRate = 0.5f;

        public override SkillType Type => SkillType.Immolation;
        public override float ResourceCostPerSecond => resourceCostPerSecond;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            var line = DamageLine(DamageType.Fire, "Damage per second", stats);
            if (stats is StatBlock s) line.Text(Num(DamagePerSecond + s.Intelligence * DamagePerIntelligence) + " (");
            line.Text($"{Num(DamagePerSecond)} + {Num(DamagePerIntelligence)} × ").Icon(GameIcons.Affix(Affix.Intelligence));
            if (stats != null) line.Text(")");
            description.Damage.Add(line);
            description.Effects.Add(AreaLine("Radius", Radius, stats));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Duration), "Hits every", TickRate, null, " s"));
        }

        protected override Immolation Configure(Immolation component, bool isNew)
        {
            component.ResourceCostPerSecond = resourceCostPerSecond;
            component.DamagePerSecond = DamagePerSecond;
            component.DamagePerIntelligence = DamagePerIntelligence;
            component.Radius = Radius;
            component.TickRate = TickRate;
            return component;
        }
    }
}
