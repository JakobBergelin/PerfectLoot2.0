using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "TeslaCoil", menuName = "Active Skills/Tesla Coil")]
    public class TeslaCoilSkill : ActiveSkill<TeslaCoil>
    {
        [Tooltip("Starts when the channel ends")]
        [SerializeField] float cooldown = 10f;
        [Tooltip("Drained per second at the start of the channel")]
        [SerializeField] float resourceCostPerSecond = 8f;
        [Tooltip("Added to the drain per second for every second of the channel")]
        [SerializeField] float resourceCostIncrease = 6f;
        [Tooltip("Meters around the caster, scaled by its area of effect")]
        public float Radius = 6f;
        [Tooltip("Lightning damage to every enemy in the field")]
        public float DamagePerSecond = 2f;
        [Tooltip("Seconds between damage ticks")]
        public float TickRate = 0.5f;

        public override SkillType Type => SkillType.TeslaCoil;
        public override float Cooldown => cooldown;
        public override float ResourceCostPerSecond => resourceCostPerSecond;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(DamageLine(DamageType.Lightning, "Damage per second", stats).Text(Num(DamagePerSecond)));
            description.Effects.Add(AreaLine("Radius", Radius, stats));
            description.Effects.Add(new SkillLine().Text($"Cost per second rises by {Num(resourceCostIncrease)} every second"));
            description.Effects.Add(new SkillLine().Text("Blocks enemy projectiles"));
            description.Effects.Add(new SkillLine().Text("You can't move while channeling"));
        }

        protected override TeslaCoil Configure(TeslaCoil component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.ResourceCostPerSecond = resourceCostPerSecond;
            component.ResourceCostIncrease = resourceCostIncrease;
            component.Radius = Radius;
            component.DamagePerSecond = DamagePerSecond;
            component.TickRate = TickRate;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }
    }
}
