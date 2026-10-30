using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Dash", menuName = "Active Skills/Dash")]
    public class DashSkill : ActiveSkill<Dash>
    {
        [SerializeField] float cooldown = 2f;

        public override SkillType Type => SkillType.Dash;
        public override float Cooldown => cooldown;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Distance), "Distance", DashSystem.Distance, null, " m"));
        }

        protected override Dash Configure(Dash component, bool isNew)
        {
            component.Cooldown = cooldown;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }
    }
}
