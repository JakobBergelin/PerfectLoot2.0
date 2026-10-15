using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Dash", menuName = "Active Skills/Dash")]
    public class DashSkill : ActiveSkill<Dash>
    {
        [SerializeField] float cooldown = 2f;

        public override SkillType Type => SkillType.Dash;
        public override float Cooldown => cooldown;

        protected override Dash Configure(Dash component, bool isNew)
        {
            component.Cooldown = cooldown;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }
    }
}
