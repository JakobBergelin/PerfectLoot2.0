using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "BallLightning", menuName = "Active Skills/Ball Lightning")]
    public class BallLightningSkill : ActiveSkill<BallLightning>
    {
        [SerializeField] float cooldown = 2f;
        [SerializeField] float resourceCost = 5f;
        [SerializeField] float resourceCostPerSecond = 10f;
        [Tooltip("Meters per second")]
        public float Speed = 12f;
        [Tooltip("Meters above the ground (or wall) below while flying")]
        public float Lift = 1f;
        [Tooltip("Damage of the landing attack, scaled by (1 + resource spent / Spent To Double)")]
        public float Damage = 5f;
        [Tooltip("Resource spent on a flight (start cost included) that doubles the landing damage")]
        public float SpentToDouble = 20f;
        [Tooltip("Radius of the landing attack")]
        public float Radius = 4f;
        [Tooltip("Knockback speed on hit, away from the landing spot")]
        public float HitForce = 4f;

        public override SkillType Type => SkillType.BallLightning;
        public override float Cooldown => cooldown;
        public override float ResourceCost => resourceCost;
        public override float ResourceCostPerSecond => resourceCostPerSecond;

        protected override BallLightning Configure(BallLightning component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.ResourceCost = resourceCost;
            component.ResourceCostPerSecond = resourceCostPerSecond;
            component.Speed = Speed;
            component.Lift = Lift;
            component.Damage = Damage;
            component.SpentToDouble = SpentToDouble;
            component.Radius = Radius;
            component.HitForce = HitForce;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }
    }
}
