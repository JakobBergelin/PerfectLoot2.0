using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Whirlwind", menuName = "Active Skills/Whirlwind")]
    public class WhirlwindSkill : ActiveSkill<Whirlwind>
    {
        [SerializeField] float resourceCostPerSecond = 12f;

        public override SkillType Type => SkillType.Whirlwind;
        public override float ResourceCostPerSecond => resourceCostPerSecond;

        protected override Whirlwind Configure(Whirlwind component, bool isNew)
        {
            component.ResourceCostPerSecond = resourceCostPerSecond;
            return component;
        }
    }
}
