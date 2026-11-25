using Equipment;
using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Charge", menuName = "Active Skills/Charge")]
    public class ChargeSkill : ActiveSkill<Charge>
    {
        [SerializeField] float cooldown = 20f;
        [Tooltip("Furthest enemy it can charge, in meters")]
        public float Range = 12f;
        [Tooltip("Meters per second")]
        public float Speed = 20f;
        [Tooltip("Resource gained on arrival")]
        public float ResourceGain = 15f;

        [Header("Buff on arrival")]
        [Tooltip("Added to attack speed: 0.5 = +50%")]
        public float AttackSpeedBonus = 0.5f;
        [Tooltip("Attacks (uses of Melee or Projectile skills) until the buff ends")]
        [Min(1)] public int BuffAttacks = 5;
        [Tooltip("Seconds until it ends even with attacks left")]
        public float BuffDuration = 10f;

        public override SkillType Type => SkillType.Charge;
        public override float Cooldown => cooldown;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Distance), "Range", Range, null, " m"));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.Resource)).Text("Resource gained: " + Num(ResourceGain)));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.AttackSpeed)).Text($"Attack speed: +{Pct(AttackSpeedBonus)} for {BuffAttacks} attacks"));
            float? duration = stats is StatBlock s ? Mathf.Max(0f, EffectApply.BuffDuration(BuffDuration, s)) : (float?)null;
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Duration), "For at most", BuffDuration, duration, " s"));
        }

        protected override Charge Configure(Charge component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.Range = Range;
            component.Speed = Speed;
            component.ResourceGain = ResourceGain;
            component.AttackSpeedBonus = AttackSpeedBonus;
            component.BuffAttacks = BuffAttacks;
            component.BuffDuration = BuffDuration;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }
    }
}
