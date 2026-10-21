using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Tornado", menuName = "Active Skills/Tornado")]
    public class TornadoSkill : ActiveSkill<Tornado>
    {
        [Tooltip("Seconds of wind-up per tornado, scaled by the attack speed stat")]
        public float AttackSpeed = 0.9f;
        [SerializeField] float resourceCost = 15f;

        [Header("Each tornado")]
        [Tooltip("Damage radius")]
        public float Radius = 2f;
        public float DamagePerSecond = 0.5f;
        [Tooltip("Added per second: weapon damage * this")]
        public float WeaponDamagePerSecond = 0.2f;
        [Tooltip("Seconds between damage ticks")]
        public float TickRate = 0.5f;
        [Tooltip("Seconds the tornado stays alive")]
        public float Lifetime = 4f;
        public float Speed = 5f;
        [Tooltip("Seconds before it picks a new random direction")]
        public float DirectionDuration = 0.7f;
        [Tooltip("Knockback speed of each damage tick, in a random direction")]
        public float HitForce = 3f;

        public override SkillType Type => SkillType.Tornado;
        public override float ResourceCost => resourceCost;

        protected override Tornado Configure(Tornado component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            component.ResourceCost = resourceCost;
            component.Radius = Radius;
            component.DamagePerSecond = DamagePerSecond;
            component.WeaponDamagePerSecond = WeaponDamagePerSecond;
            component.TickRate = TickRate;
            component.Lifetime = Lifetime;
            component.Speed = Speed;
            component.DirectionDuration = DirectionDuration;
            component.HitForce = HitForce;
            return component;
        }
    }
}
