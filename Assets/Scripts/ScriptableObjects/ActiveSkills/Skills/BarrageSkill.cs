using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Barrage", menuName = "Active Skills/Barrage")]
    public class BarrageSkill : ActiveSkill<Barrage>
    {
        [Tooltip("Seconds between projectiles")]
        public float AttackSpeed = 0.3f;

        public override SkillType Type => SkillType.Barrage;

        protected override Barrage Configure(Barrage component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            return component;
        }
    }
}
