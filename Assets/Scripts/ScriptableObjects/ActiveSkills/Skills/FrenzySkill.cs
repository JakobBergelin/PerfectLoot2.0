using Equipment;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// A toggle. While it's on, the Frenzy buff starts whenever your resource is full. The buff drains a share of your max
    /// resource every second and ends when the resource runs low. While it lasts, each melee attack also strikes a second
    /// enemy nearby, with the element of the melee skill used.
    /// </summary>
    [CreateAssetMenu(fileName = "Frenzy", menuName = "Active Skills/Frenzy")]
    public class FrenzySkill : ActiveSkill<Frenzy>
    {
        [Tooltip("Share of your max resource drained per second while the buff is active (0.25 = 25%), lowered by resource cost reduction")]
        [Range(0f, 1f)] public float DrainPerSecond = 0.25f;
        [Tooltip("The buff ends when your resource drops below this share of its max (0.05 = 5%)")]
        [Range(0f, 1f)] public float EndThreshold = 0.05f;
        [Tooltip("Meters from you the second enemy can be")]
        public float StrikeRange = 5f;
        [Tooltip("The extra strike's hit radius in meters, scaled by area of effect")]
        public float StrikeRadius = 1f;
        [Tooltip("The extra strike's damage: weapon damage times this")]
        public float WeaponDamageMultiplier = 1f;

        public override SkillType Type => SkillType.Frenzy;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(WeaponDamageLine(DamageType.Physical, "Extra strike", 0f, 0f, WeaponDamageMultiplier, stats).Text(", the melee skill's element"));
            float drain = stats is StatBlock s ? SkillUtil.Cost(DrainPerSecond, s) : DrainPerSecond;
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.Resource)).Text($"Starts at full resource, drains {Pct(drain)} of max per second"));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.Resource)).Text($"Ends below {Pct(EndThreshold)} of max resource"));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Distance), "Second enemy within", StrikeRange, null, " m"));
            description.Effects.Add(AreaLine("Strike radius", StrikeRadius, stats));
        }

        protected override Frenzy Configure(Frenzy component, bool isNew)
        {
            component.DrainPerSecond = DrainPerSecond;
            component.EndThreshold = EndThreshold;
            component.StrikeRange = StrikeRange;
            component.StrikeRadius = StrikeRadius;
            component.WeaponDamageMultiplier = WeaponDamageMultiplier;
            return component;
        }
    }
}
