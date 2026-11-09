using Unity.Mathematics;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// Throws a tumbling hammer that starts slow and keeps speeding up. It deals no damage itself: where it hits an enemy or
    /// a wall it bursts, dealing lightning damage in an area.
    /// </summary>
    [CreateAssetMenu(fileName = "CursedHammer", menuName = "Active Skills/Cursed Hammer")]
    public class CursedHammerSkill : ActiveSkill<CursedHammer>
    {
        [Tooltip("Seconds before the next cast (shared by all repeated attacks), scaled by the attack speed stat")]
        public float AttackSpeed = 1.2f;
        [SerializeField] float resourceCost = 20f;

        [Header("Flight")]
        [Tooltip("Meters per second when thrown, scaled by the projectile speed stat")]
        public float StartSpeed = 4f;
        [Tooltip("Meters per second gained every second in the air")]
        public float Acceleration = 8f;
        [Tooltip("It stops speeding up at this many meters per second")]
        public float MaxSpeed = 35f;
        [Tooltip("How close an enemy has to come to the hammer to be hit, in meters")]
        public float HitRadius = 0.5f;
        [Tooltip("Degrees it tumbles per meter flown, so it spins faster as it speeds up")]
        public float SpinPerMeter = 120f;

        [Header("Burst (where it hits)")]
        public float Damage = 12f;
        public float Radius = 3f;

        public override SkillType Type => SkillType.CursedHammer;
        public override float ResourceCost => resourceCost;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(DamageLine(DamageType.Lightning, "Burst damage", stats).Text(Num(Damage)));
            description.Effects.Add(AreaLine("Burst radius", Radius, stats));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Distance), "Speed", StartSpeed, null, " m/s"));
            description.Effects.Add(new SkillLine().Text($"Gains {Num(Acceleration)} m/s every second, up to {Num(MaxSpeed)} m/s"));
            description.Effects.Add(AttackTimeLine("Time between casts", AttackSpeed, stats));
        }

        protected override CursedHammer Configure(CursedHammer component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            component.ResourceCost = resourceCost;
            component.StartSpeed = StartSpeed;
            component.Acceleration = Acceleration;
            component.MaxSpeed = MaxSpeed;
            component.HitRadius = HitRadius;
            component.SpinPerMeter = math.radians(SpinPerMeter);
            component.Damage = Damage;
            component.Radius = Radius;
            return component;
        }
    }
}
