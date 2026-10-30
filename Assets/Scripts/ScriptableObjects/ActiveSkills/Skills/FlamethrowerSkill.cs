using Equipment;
using Unity.Mathematics;
using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Flamethrower", menuName = "Active Skills/Flamethrower")]
    public class FlamethrowerSkill : ActiveSkill<Flamethrower>
    {
        [Tooltip("Paid once when the spray starts")]
        [SerializeField] float resourceCost = 3f;
        [Tooltip("Drained while spraying")]
        [SerializeField] float resourceCostPerSecond = 12f;
        public float FlamesPerSecond = 15f;
        [Tooltip("Degrees each side of the aim a flame can leave at")]
        public float SpreadAngle = 12f;
        [Tooltip("Movement speed taken off while spraying (0.4 = 40%)")]
        public float Slow = 0.4f;

        [Header("Each flame")]
        public float Speed = 9f;
        [Tooltip("Seconds a flame burns")]
        public float Lifetime = 0.7f;
        [Tooltip("Damage radius when it leaves; grows to End Radius over its lifetime. Both scaled by the area of effect stat")]
        public float StartRadius = 0.3f;
        public float EndRadius = 1f;
        [Tooltip("To every enemy inside the flame; overlapping flames add up")]
        public float DamagePerSecond = 1.5f;
        [Tooltip("Seconds between damage ticks")]
        public float TickRate = 0.25f;
        [Tooltip("Knockback speed of each damage tick, away from the flame")]
        public float HitForce = 0f;

        public override SkillType Type => SkillType.Flamethrower;
        public override float ResourceCost => resourceCost;
        public override float ResourceCostPerSecond => resourceCostPerSecond;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(DamageLine(DamageType.Fire, "Damage per second per flame").Text(Num(DamagePerSecond)));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Duration), "Flames last", Lifetime, null, " s"));
            // Grows from start to end radius, both scaled by the AoE stat as FlamethrowerSystem spawns them
            float scale = stats is StatBlock s ? s.AreaMultiplier : 1f;
            var radius = new SkillLine().Icon(GameIcons.Affix(Affix.AreaOfEffect)).Text($"Flame radius: {Num(StartRadius * scale)} → {Num(EndRadius * scale)} m");
            if (!Mathf.Approximately(scale, 1f)) radius.Text(" ").Muted($"({Num(StartRadius)} → {Num(EndRadius)} m)");
            description.Effects.Add(radius);
            description.Effects.Add(ValueLine(GameIcons.Affix(Affix.MovementSpeed), "Slowed while spraying", Slow * 100f, null, "%"));
        }

        protected override Flamethrower Configure(Flamethrower component, bool isNew)
        {
            component.ResourceCost = resourceCost;
            component.ResourceCostPerSecond = resourceCostPerSecond;
            component.FlamesPerSecond = FlamesPerSecond;
            component.Spread = math.radians(SpreadAngle);
            component.Slow = Slow;
            component.Speed = Speed;
            component.Lifetime = Lifetime;
            component.StartRadius = StartRadius;
            component.EndRadius = EndRadius;
            component.DamagePerSecond = DamagePerSecond;
            component.TickRate = TickRate;
            component.HitForce = HitForce;
            return component;
        }
    }
}
