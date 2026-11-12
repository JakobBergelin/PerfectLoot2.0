using UnityEngine;

namespace EnemySkills
{
    /// <summary>A fan of missiles that home in on the enemy's target.</summary>
    [CreateAssetMenu(fileName = "HomingVolley", menuName = "Enemy Skills/Homing Volley")]
    public class EnemyHomingVolleySkill : EnemySkill<EnemyHomingVolley>
    {
        [Header("Missiles")]
        [Min(1)] public int Count = 5;
        [Tooltip("Degrees from one side of the fan to the other")]
        public float Spread = 120f;
        public float Damage = 6f;
        public DamageType DamageType = DamageType.Poison;
        public float ProjectileSpeed = 9f;
        [Tooltip("Hit radius of each missile")]
        public float Radius = 0.35f;
        [Tooltip("Radians per second a missile turns toward the target at first")]
        public float MissileTurnSpeed = 1.5f;
        [Tooltip("Added to the turn speed every second")]
        public float MissileTurnAcceleration = 1.5f;
        [Tooltip("Meters a missile flies before it fizzles")]
        public float MaxDistance = 30f;
        public float HitForce = 1f;

        public override EnemySkillType Type => EnemySkillType.HomingVolley;

        protected override EnemyHomingVolley Configure() => new EnemyHomingVolley
        {
            Count = Count,
            Spread = Spread,
            Damage = Damage,
            Type = DamageType,
            ProjectileSpeed = ProjectileSpeed,
            Radius = Radius,
            TurnSpeed = MissileTurnSpeed,
            TurnAcceleration = MissileTurnAcceleration,
            MaxDistance = MaxDistance,
            HitForce = HitForce,
        };
    }
}
