using Equipment;
using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Dash", menuName = "Active Skills/Dash")]
    public class DashSkill : ActiveSkill<Dash>
    {
        [SerializeField] float cooldown = 2f;
        [Tooltip("The cooldown is divided by 1 + Agility × this: 0.01 = 100 Agility halves it")]
        [SerializeField] float cooldownPerAgility = 0.01f;

        public override SkillType Type => SkillType.Dash;
        public override float Cooldown => cooldown;
        public override float CooldownFor(in StatBlock stats) => DashSystem.CooldownFor(cooldown, cooldownPerAgility, stats);

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Distance), "Distance", DashSystem.Distance, null, " m"));

            // Agility's share of the cooldown: the reduction with the caster's Agility, else the rate
            var agility = new SkillLine().Icon(GameIcons.Affix(Affix.Agility));
            if (stats is StatBlock s)
                agility.Text($"Agility: -{Pct(1f - 1f / (1f + Mathf.Max(0f, s.Agility * cooldownPerAgility)))} cooldown");
            else
                agility.Text($"Cooldown: {Pct(cooldownPerAgility)} faster per Agility");
            description.Effects.Add(agility);
        }

        protected override Dash Configure(Dash component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.CooldownPerAgility = cooldownPerAgility;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }
    }
}
