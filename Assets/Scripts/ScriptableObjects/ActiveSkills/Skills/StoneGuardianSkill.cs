using UnityEngine;

namespace ActiveSkills
{
    /// <summary>A sturdy companion that walks ahead of the caster and draws enemies to itself.</summary>
    [CreateAssetMenu(fileName = "StoneGuardian", menuName = "Active Skills/Stone Guardian")]
    public class StoneGuardianSkill : CompanionSkill
    {
        public override SkillType Type => SkillType.StoneGuardian;
    }
}
