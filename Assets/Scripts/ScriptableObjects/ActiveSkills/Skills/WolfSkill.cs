using UnityEngine;

namespace ActiveSkills
{
    /// <summary>A fast, frail companion that roams around the caster. Pack Instinct raises the critical chance of allies near it.</summary>
    [CreateAssetMenu(fileName = "Wolf", menuName = "Active Skills/Wolf")]
    public class WolfSkill : CompanionSkill
    {
        public override SkillType Type => SkillType.Wolf;
    }
}
