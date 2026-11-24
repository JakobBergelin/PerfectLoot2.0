using UnityEngine;

namespace EnemySkills
{
    /// <summary>
    /// A jump onto the spot the target stood on when the cast began. Give it a Circle telegraph: its Size is the landing's
    /// radius, so the hit always matches the marker. It crouches for the start of the telegraph and is in the air for the
    /// last Air Time of it, landing as the marker fills.
    /// </summary>
    [CreateAssetMenu(fileName = "Leap", menuName = "Enemy Skills/Leap")]
    public class EnemyLeapSkill : EnemySkill<EnemyLeap>
    {
        [Header("Landing (its radius is the telegraph's Size)")]
        public float Damage = 7f;
        public DamageType DamageType = DamageType.Physical;
        [Tooltip("Push on hit, away from the landing spot")]
        public float HitForce = 3f;

        [Header("Jump")]
        [Tooltip("Seconds in the air, at the end of the telegraph. No longer than the telegraph's Duration.")]
        public float AirTime = 0.6f;
        [Tooltip("Meters high at the top of the arc")]
        public float Height = 3f;

        public override EnemySkillType Type => EnemySkillType.Leap;

        protected override EnemyLeap Configure() => new EnemyLeap
        {
            Damage = Damage,
            Type = DamageType,
            Radius = Telegraph.Size,
            HitForce = HitForce,
            AirTime = Mathf.Min(AirTime, Telegraph.Duration),
            Height = Height,
        };
    }
}
