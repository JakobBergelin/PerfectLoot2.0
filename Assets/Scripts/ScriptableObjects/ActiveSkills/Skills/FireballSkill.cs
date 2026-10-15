using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Fireball", menuName = "Active Skills/Fireball")]
    public class FireballSkill : ActiveSkill<FireBall>
    {
        [Tooltip("Seconds of wind-up per fireball, scaled by the attack speed stat")]
        public float AttackSpeed = 0.5f;
        [SerializeField] float resourceCost = 2f;

        public override SkillType Type => SkillType.Fireball;
        public override float ResourceCost => resourceCost;

        protected override FireBall Configure(FireBall component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            component.ResourceCost = resourceCost;
            return component;
        }
    }
}
