using UnityEngine;

namespace EnemySkills
{
    /// <summary>
    /// A blast around the enemy that kills it. Give it a Circle telegraph with On Self, sized like Radius, so players can
    /// get out in time.
    /// </summary>
    [CreateAssetMenu(fileName = "SelfDestruct", menuName = "Enemy Skills/Self Destruct")]
    public class EnemySelfDestructSkill : EnemySkill<EnemySelfDestruct>
    {
        [Header("Blast")]
        public float Damage = 20f;
        public DamageType DamageType = DamageType.Poison;
        [Tooltip("Meters around the enemy")]
        public float Radius = 3f;
        [Tooltip("Push on hit, away from the enemy")]
        public float HitForce;

        public override EnemySkillType Type => EnemySkillType.SelfDestruct;

        protected override EnemySelfDestruct Configure() => new EnemySelfDestruct
        {
            Damage = Damage,
            Type = DamageType,
            Radius = Radius,
            HitForce = HitForce,
        };
    }
}
