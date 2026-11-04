using UnityEngine;

namespace EnemySkills
{
    /// <summary>A basic melee hit in front of the enemy. Its reach is Max Range: the hit has half that radius.</summary>
    [CreateAssetMenu(fileName = "Melee", menuName = "Enemy Skills/Melee")]
    public class EnemyMeleeSkill : EnemySkill<EnemyMelee>
    {
        [Header("Hit")]
        public float Damage = 0.5f;
        public DamageType DamageType = DamageType.Physical;
        [Tooltip("Push on hit")]
        public float HitForce;

        public override EnemySkillType Type => EnemySkillType.Melee;

        protected override EnemyMelee Configure() => new EnemyMelee
        {
            Damage = Damage,
            Type = DamageType,
            Reach = MaxRange,
            HitForce = HitForce,
        };
    }
}
