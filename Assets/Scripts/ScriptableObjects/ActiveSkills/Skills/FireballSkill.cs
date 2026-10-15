using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Fireball", menuName = "Active Skills/Fireball")]
    public class FireballSkill : ActiveSkill<FireBall>
    {
        [Tooltip("Seconds of wind-up per fireball, scaled by the attack speed stat")]
        public float AttackSpeed = 0.5f;
        [SerializeField] float resourceCost = 2f;
        public float Damage = 2f;
        public float ProjectileSpeed = 15f;
        [Tooltip("Hit radius of the fireball")]
        public float Radius = 0.25f;

        public override SkillType Type => SkillType.Fireball;
        public override float ResourceCost => resourceCost;

        protected override FireBall Configure(FireBall component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            component.ResourceCost = resourceCost;
            component.Damage = Damage;
            component.ProjectileSpeed = ProjectileSpeed;
            component.Radius = Radius;
            return component;
        }
    }
}
