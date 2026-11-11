using UnityEngine;

namespace EnemySkills
{
    /// <summary>
    /// A beam from the enemy toward its target. Give it a Line telegraph: its Size is the beam's length and its Width
    /// the beam's width, so the hit always matches the marker.
    /// </summary>
    [CreateAssetMenu(fileName = "Beam", menuName = "Enemy Skills/Beam")]
    public class EnemyBeamSkill : EnemySkill<EnemyBeam>
    {
        [Header("Beam (its length and width are the telegraph's Size and Width)")]
        public float Damage = 20f;
        public DamageType DamageType = DamageType.Lightning;
        [Tooltip("Meters high, so it reaches units of any size")]
        public float Height = 4f;
        [Tooltip("Push on hit, sideways out of the beam")]
        public float HitForce = 2f;

        public override EnemySkillType Type => EnemySkillType.Beam;

        protected override EnemyBeam Configure() => new EnemyBeam
        {
            Damage = Damage,
            Type = DamageType,
            Length = Telegraph.Size,
            Width = Telegraph.Width,
            Height = Height,
            HitForce = HitForce,
        };
    }
}
