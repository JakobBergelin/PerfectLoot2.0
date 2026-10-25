using Unity.Mathematics;
using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Whirlwind", menuName = "Active Skills/Whirlwind")]
    public class WhirlwindSkill : ActiveSkill<Whirlwind>
    {
        // The weapon hit circle is placed WeaponReach in front of the player
        [SerializeField] float resourceCostPerSecond = 12f;
        public float WeaponDamageMultiplier = 0.4f;
        [Tooltip("Full turns per second, scaled by the attack speed stat")]
        public float RotationsPerSecond = 2f;
        [Tooltip("Hit radius of the spinning weapon")]
        public float AttackRadius = 0.9f;
        [Tooltip("Seconds of spinning before the weapon starts hitting")]
        public float SpinUpTime = 0.4f;
        [Tooltip("Knockback speed of each hit, away from the weapon")]
        public float HitForce = 3f;
        [Tooltip("Seconds between melee ticks while spinning: each counts as a melee attack (e.g. for Echoing Blade)")]
        public float TickInterval = 0.3f;

        public override SkillType Type => SkillType.Whirlwind;
        public override float ResourceCostPerSecond => resourceCostPerSecond;

        protected override Whirlwind Configure(Whirlwind component, bool isNew)
        {
            component.ResourceCostPerSecond = resourceCostPerSecond;
            component.WeaponDamageMultiplier = WeaponDamageMultiplier;
            component.RotationSpeed = RotationsPerSecond * 2f * math.PI;
            component.AttackRadius = AttackRadius;
            component.SpinUpTime = SpinUpTime;
            component.HitForce = HitForce;
            component.TickInterval = TickInterval;
            return component;
        }
    }
}
