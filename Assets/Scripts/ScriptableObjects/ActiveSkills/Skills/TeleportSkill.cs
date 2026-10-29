using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Teleport", menuName = "Active Skills/Teleport")]
    public class TeleportSkill : ActiveSkill<Teleport>
    {
        [SerializeField] float cooldown = 5f;
        [SerializeField] float resourceCost = 10f;
        [Tooltip("Furthest it lands from the caster, in meters")]
        public float Range = 12f;
        [Tooltip("Seconds standing still between pressing it and landing")]
        public float CastTime = 0.2f;

        public override SkillType Type => SkillType.Teleport;
        public override float Cooldown => cooldown;
        public override float ResourceCost => resourceCost;

        protected override Teleport Configure(Teleport component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.ResourceCost = resourceCost;
            component.Range = Range;
            component.CastTime = CastTime;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }
    }
}
