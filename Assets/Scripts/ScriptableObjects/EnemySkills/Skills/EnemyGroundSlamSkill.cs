using UnityEngine;

namespace EnemySkills
{
    /// <summary>
    /// A blast on the spot the target stood on when the cast began. Give it a Circle telegraph: its Size is the blast's
    /// radius, so the hit always matches the marker. A Resource Cost makes it wait for resource.
    /// </summary>
    [CreateAssetMenu(fileName = "GroundSlam", menuName = "Enemy Skills/Ground Slam")]
    public class EnemyGroundSlamSkill : EnemySkill<EnemyGroundSlam>
    {
        [Header("Blast (its radius is the telegraph's Size)")]
        public float Damage = 25f;
        public DamageType DamageType = DamageType.Physical;
        [Tooltip("Push on hit, away from the blast's centre")]
        public float HitForce = 4f;

        public override EnemySkillType Type => EnemySkillType.GroundSlam;

        protected override EnemyGroundSlam Configure() => new EnemyGroundSlam
        {
            Damage = Damage,
            Type = DamageType,
            Radius = Telegraph.Size,
            HitForce = HitForce,
        };
    }
}
