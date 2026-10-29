using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "CaptainsShield", menuName = "Active Skills/Captain's Shield")]
    public class CaptainsShieldSkill : ActiveSkill<CaptainsShield>
    {
        [SerializeField] float resourceCost = 5f;
        [Tooltip("Damage per hit, scaled by (1 + block chance)")]
        public float Damage = 3f;
        public float ProjectileSpeed = 18f;
        [Tooltip("Hit radius of the shield")]
        public float Radius = 0.5f;
        [Tooltip("Knockback speed on hit, away from the shield")]
        public float HitForce = 2f;
        [Tooltip("Targets after the first, before the Bounces stat")]
        public int Bounces = 1;
        [Tooltip("Radians per second the shield turns, at the start and after each new target")]
        public float TurnSpeed = 10f;
        [Tooltip("How fast the turn speed grows, radians per second per second")]
        public float TurnAcceleration = 20f;
        [Tooltip("How far it flies toward the cursor before returning when there's no enemy to home on")]
        public float NoTargetDistance = 10f;

        public override SkillType Type => SkillType.CaptainsShield;
        public override float ResourceCost => resourceCost;

        protected override CaptainsShield Configure(CaptainsShield component, bool isNew)
        {
            component.ResourceCost = resourceCost;
            component.Damage = Damage;
            component.ProjectileSpeed = ProjectileSpeed;
            component.Radius = Radius;
            component.HitForce = HitForce;
            component.Bounces = Bounces;
            component.TurnSpeed = TurnSpeed;
            component.TurnAcceleration = TurnAcceleration;
            component.NoTargetDistance = NoTargetDistance;
            return component;
        }
    }
}
