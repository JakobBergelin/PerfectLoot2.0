using UnityEngine;

namespace ActiveSkills
{
    /// <summary>A slow poison arrow that always flies Range meters, poisoning everything around it on the way.</summary>
    [CreateAssetMenu(fileName = "PlagueArrow", menuName = "Active Skills/Plague Arrow")]
    public class PlagueArrowSkill : ActiveSkill<PlagueArrow>
    {
        [Tooltip("Seconds before the next attack after each arrow (shared by all repeated attacks), scaled by the attack speed stat")]
        public float AttackSpeed = 1.2f;
        [SerializeField] float resourceCost = 20f;

        [Header("Each arrow")]
        [Tooltip("Poison damage per second to enemies within Radius")]
        public float DamagePerSecond = 1f;
        [Tooltip("Seconds between damage ticks")]
        public float TickRate = 0.5f;
        [Tooltip("Damage radius around the arrow")]
        public float Radius = 1.5f;
        [Tooltip("Meters per second, before the caster's projectile speed (slower: longer on the way, more damage)")]
        public float Speed = 3f;
        [Tooltip("Meters it always flies, whatever its speed")]
        public float Range = 30f;

        public override SkillType Type => SkillType.PlagueArrow;
        public override float ResourceCost => resourceCost;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(DamageLine(DamageType.Poison, "Damage per second", stats).Text(Num(DamagePerSecond)));
            description.Effects.Add(new SkillLine().Text($"Always flies {Num(Range)} m"));
            description.Effects.Add(new SkillLine().Text("Slower projectiles poison for longer"));
            description.Effects.Add(AreaLine("Radius", Radius, stats));
            description.Effects.Add(AttackTimeLine("Time between arrows", AttackSpeed, stats));
        }

        protected override PlagueArrow Configure(PlagueArrow component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            component.ResourceCost = resourceCost;
            component.DamagePerSecond = DamagePerSecond;
            component.TickRate = TickRate;
            component.Radius = Radius;
            component.Speed = Speed;
            component.Range = Range;
            return component;
        }
    }
}
