using UnityEngine;

namespace EnemySkills
{
    /// <summary>One projectile flying straight at where the target was, or a burst of Count sweeping across it. Scaled by the enemy's damage % and conversion, like the player's.</summary>
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

        [Header("Burst")]
        [Tooltip("Shots per use, one after another. 1: a single shot.")]
        [Min(1)] public int Count = 1;
        [Tooltip("Seconds between the shots of a burst")]
        public float ShotInterval = 0.3f;
        [Tooltip("Degrees: the first shot flies this far left of the target, the last this far right, the rest evenly between")]
        public float SweepAngle;

        public override EnemySkillType Type => EnemySkillType.Projectile;

        protected override EnemyProjectile Configure() => new EnemyProjectile
        {
            Damage = Damage,
            Type = DamageType,
            ProjectileSpeed = ProjectileSpeed,
            Radius = Radius,
            HitForce = HitForce,
            MaxDistance = MaxDistance,
            Count = Count,
            ShotInterval = ShotInterval,
            SweepAngle = Mathf.Deg2Rad * SweepAngle,
            ShotsFired = Count,
        };
    }
}
