using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "FistsOfThunder", menuName = "Active Skills/Fists Of Thunder")]
    public class FistsOfThunderSkill : ActiveSkill<FistsOfThunder>
    {
        public override SkillType Type => SkillType.FistOfThunder;

        protected override FistsOfThunder Configure(FistsOfThunder component, bool isNew) => component;
    }
}
