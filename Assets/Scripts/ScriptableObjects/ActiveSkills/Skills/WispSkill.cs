using UnityEngine;

namespace ActiveSkills
{
    /// <summary>A companion that doesn't attack: it runs from enemies while staying close, and Life Link shares its healing with a player.</summary>
    [CreateAssetMenu(fileName = "Wisp", menuName = "Active Skills/Wisp")]
    public class WispSkill : CompanionSkill
    {
        public override SkillType Type => SkillType.Wisp;
    }
}
