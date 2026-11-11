using Equipment;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// A toggle: off, clockwise, counter-clockwise. A band of wind circles you, bending every projectile in it (yours too)
    /// and chilling enemies in it. Drains resource while on, and pauses without enough.
    /// </summary>
    [CreateAssetMenu(fileName = "GaleWard", menuName = "Active Skills/Gale Ward")]
    public class GaleWardSkill : ActiveSkill<GaleWard>
    {
        [SerializeField] float resourceCostPerSecond = 3f;
        [Tooltip("Meters from you the wind starts, scaled by area of effect")]
        [Min(0f)] public float InnerRadius = 6f;
        [Tooltip("Meters from you the wind ends, scaled by area of effect")]
        [Min(0.1f)] public float OuterRadius = 9f;
        [Tooltip("Degrees per second a projectile in the wind turns toward its way")]
        public float TurnSpeed = 120f;
        [Tooltip("Chill stacks each enemy in the wind gets every tick")]
        [Min(0)] public int ChillPerTick = 1;
        [Tooltip("Seconds between chills")]
        [Min(0.05f)] public float TickRate = 0.5f;

        public override SkillType Type => SkillType.GaleWard;
        public override float ResourceCostPerSecond => resourceCostPerSecond;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Effects.Add(new SkillLine().Text("Press to switch: off, clockwise, counter-clockwise"));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.AreaOfEffect)).Text(stats is StatBlock s
                ? $"Wind from {Num(InnerRadius * s.AreaMultiplier)} to {Num(OuterRadius * s.AreaMultiplier)} m around you"
                : $"Wind from {Num(InnerRadius)} to {Num(OuterRadius)} m around you"));
            description.Effects.Add(new SkillLine().Text($"Bends every projectile in it by {Num(TurnSpeed)}° per second"));
            description.Effects.Add(new SkillLine().Icon(GameIcons.DamageType(DamageType.Cold)).Text($"Chills enemies in it by {ChillPerTick} every {Num(TickRate)} s"));
        }

        protected override GaleWard Configure(GaleWard component, bool isNew)
        {
            component.ResourceCostPerSecond = resourceCostPerSecond;
            component.InnerRadius = InnerRadius;
            component.OuterRadius = OuterRadius;
            component.TurnSpeed = TurnSpeed;
            component.ChillPerTick = ChillPerTick;
            component.TickRate = TickRate;
            return component;
        }
    }
}
