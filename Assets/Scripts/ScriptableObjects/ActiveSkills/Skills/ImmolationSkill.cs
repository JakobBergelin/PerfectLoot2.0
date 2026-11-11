using Equipment;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// A toggle. While it's on it costs a share of max health every second and enemies around the caster take fire damage
    /// based on max health. When paying would leave the caster under 1 health it stays on but pauses.
    /// </summary>
    [CreateAssetMenu(fileName = "Immolation", menuName = "Active Skills/Immolation")]
    public class ImmolationSkill : ActiveSkill<Immolation>
    {
        [Tooltip("Fire damage per second to each enemy in range as a share of your max health (0.1 = 10%), before the damage %")]
        public float DamagePerMaxHealth = 0.1f;
        [Tooltip("Share of your max health lost per second while it's on (0.02 = 2%)")]
        public float HealthCostPerSecond = 0.02f;
        [Tooltip("Meters around you, scaled by area of effect")]
        public float Radius = 4f;
        [Tooltip("Seconds between damage ticks")]
        [Min(0.05f)] public float TickRate = 0.5f;

        public override SkillType Type => SkillType.Immolation;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(DamageLine(DamageType.Fire, "Damage per second", stats).Text($"{Pct(DamagePerMaxHealth)} of your max health"));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.Health)).Text($"Costs {Pct(HealthCostPerSecond)} of your max health per second"));
            description.Effects.Add(AreaLine("Radius", Radius, stats));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Duration), "Hits every", TickRate, null, " s"));
        }

        protected override Immolation Configure(Immolation component, bool isNew)
        {
            component.DamagePerMaxHealth = DamagePerMaxHealth;
            component.HealthCostPerSecond = HealthCostPerSecond;
            component.Radius = Radius;
            component.TickRate = TickRate;
            return component;
        }
    }
}
