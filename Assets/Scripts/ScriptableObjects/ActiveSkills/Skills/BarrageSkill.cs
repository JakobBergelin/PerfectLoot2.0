using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Barrage", menuName = "Active Skills/Barrage")]
    public class BarrageSkill : ActiveSkill<Barrage>
    {
        [Tooltip("Seconds between projectiles")]
        public float AttackSpeed = 0.3f;
        public float Damage = 3f;
        public float ProjectileSpeed = 7f;
        [Tooltip("Hit radius of each projectile")]
        public float Radius = 0.25f;

        [Header("Homing")]
        public float HomingTurnSpeed = 1.5f;
        public float HomingTurnAcceleration = 1.7f;

        public override SkillType Type => SkillType.Barrage;

        protected override Barrage Configure(Barrage component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            component.Damage = Damage;
            component.ProjectileSpeed = ProjectileSpeed;
            component.Radius = Radius;
            component.HomingTurnSpeed = HomingTurnSpeed;
            component.HomingTurnAcceleration = HomingTurnAcceleration;
            return component;
        }
    }
}
