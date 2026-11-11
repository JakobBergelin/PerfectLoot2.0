using Equipment;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>A volley of arrows spread over a cone toward the cursor, each one a weapon damage hit. Costs resource.</summary>
    [CreateAssetMenu(fileName = "Multishot", menuName = "Active Skills/Multishot")]
    public class MultishotSkill : ActiveSkill<Multishot>
    {
        [Tooltip("Seconds before the next volley (shared by all repeated attacks), scaled by the attack speed stat")]
        public float AttackSpeed = 0.6f;
        [SerializeField] float resourceCost = 8f;
        [Tooltip("Arrows in each volley")]
        [Min(1)] public int ArrowCount = 6;
        [Tooltip("Degrees to each side of your aim the arrows spread over, evenly")]
        [Range(0f, 90f)] public float HalfAngle = 30f;
        [Tooltip("Each arrow's physical damage: weapon damage * this")]
        public float WeaponDamageMultiplier = 0.4f;
        public float ProjectileSpeed = 20f;
        [Tooltip("Meters each arrow flies")]
        [Min(0.1f)] public float Range = 18f;
        [Tooltip("Hit radius of each arrow")]
        public float Radius = 0.25f;
        [Tooltip("Knockback speed on hit, away from the arrow")]
        public float HitForce = 1f;

        public override SkillType Type => SkillType.Multishot;
        public override float ResourceCost => resourceCost;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(WeaponDamageLine(DamageType.Physical, "Damage per arrow", 0f, 0f, WeaponDamageMultiplier, stats));
            description.Effects.Add(new SkillLine().Text($"{ArrowCount} arrows over {Num(HalfAngle * 2f)}°"));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Distance), "Range", Range, null, " m"));
            description.Effects.Add(AttackTimeLine("Time between volleys", AttackSpeed, stats));
        }

        protected override Multishot Configure(Multishot component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            component.ResourceCost = resourceCost;
            component.ArrowCount = ArrowCount;
            component.HalfAngle = HalfAngle;
            component.WeaponDamageMultiplier = WeaponDamageMultiplier;
            component.ProjectileSpeed = ProjectileSpeed;
            component.Range = Range;
            component.Radius = Radius;
            component.HitForce = HitForce;
            return component;
        }
    }
}
