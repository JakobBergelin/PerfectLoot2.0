using Equipment;
using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Shadowstep", menuName = "Active Skills/Shadowstep")]
    public class ShadowstepSkill : ActiveSkill<Shadowstep>
    {
        [SerializeField] float cooldown = 60f;
        [Tooltip("Seconds of the Shadowstep buff")]
        public float Duration = 20f;
        [Tooltip("Added to attack speed while the buff lasts: 0.15 = +15%")]
        public float AttackSpeedBonus = 0.15f;
        [Tooltip("Furthest enemy a melee skill jumps to while the buff lasts, in meters")]
        public float Range = 30f;

        public override SkillType Type => SkillType.Shadowstep;
        public override float Cooldown => cooldown;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            float? duration = stats is StatBlock s ? Mathf.Max(0f, EffectApply.BuffDuration(Duration, s)) : (float?)null;
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Duration), "Shadowstep for", Duration, duration, " s"));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.AttackSpeed)).Text("Attack speed: +" + Pct(AttackSpeedBonus)));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Distance), "Melee skills jump to the target", Range, null, " m"));
        }

        protected override Shadowstep Configure(Shadowstep component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.Duration = Duration;
            component.AttackSpeedBonus = AttackSpeedBonus;
            component.Range = Range;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }
    }
}
