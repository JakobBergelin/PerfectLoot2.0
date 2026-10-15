using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Tornado", menuName = "Active Skills/Tornado")]
    public class TornadoSkill : ActiveSkill<Tornado>
    {
        [Tooltip("Seconds of wind-up per tornado, scaled by the attack speed stat")]
        public float AttackSpeed = 0.9f;
        [SerializeField] float resourceCost = 15f;

        public override SkillType Type => SkillType.Tornado;
        public override float ResourceCost => resourceCost;

        protected override Tornado Configure(Tornado component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            component.ResourceCost = resourceCost;
            return component;
        }
    }
}
