using UnityEngine;

namespace EnemySkills
{
    /// <summary>One projectile flying straight at where the target was. Scaled by the enemy's damage % and conversion, like the player's.</summary>
    [CreateAssetMenu(fileName = "Projectile", menuName = "Enemy Skills/Projectile")]
    public class EnemyProjectileSkill : EnemySkill<EnemyProjectile>
    {
        [Header("Projectile")]
        public float Damage = 1f;
        public DamageType DamageType = DamageType.Physical;
        [Tooltip("Meters per second")]
        public float ProjectileSpeed = 12f;
        [Tooltip("Hit radius in meters")]
        public float Radius = 0.3f;
        [Tooltip("Push on hit")]
        public float HitForce = 1f;
        [Tooltip("Meters it flies before it's gone")]
        public float MaxDistance = 20f;

        public override EnemySkillType Type => EnemySkillType.Projectile;

        protected override EnemyProjectile Configure() => new EnemyProjectile
        {
            Damage = Damage,
            Type = DamageType,
            ProjectileSpeed = ProjectileSpeed,
            Radius = Radius,
            HitForce = HitForce,
            MaxDistance = MaxDistance,
        };
    }
}
