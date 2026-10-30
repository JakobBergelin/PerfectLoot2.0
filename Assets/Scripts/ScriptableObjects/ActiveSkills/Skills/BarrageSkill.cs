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

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(DamageLine(DamageType.Physical, "Damage per missile", stats).Text(Num(Damage)));
            description.Effects.Add(AreaLine("Hit radius", Radius, stats));
            description.Effects.Add(WindUpLine("Time per missile", AttackSpeed, stats));
        }

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
