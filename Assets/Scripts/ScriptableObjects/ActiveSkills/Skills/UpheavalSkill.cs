using UnityEngine;

namespace ActiveSkills
{
    /// <summary>Slams the ground in front of you; a moment later the ground heaves around it in a bigger circle, throwing enemies outward.</summary>
    [CreateAssetMenu(fileName = "Upheaval", menuName = "Active Skills/Upheaval")]
    public class UpheavalSkill : ActiveSkill<Upheaval>
    {
        [Tooltip("Seconds before the next slam (shared by all repeated attacks), scaled by the attack speed stat")]
        public float AttackSpeed = 1.4f;
        [SerializeField] float resourceCost = 25f;

        [Header("Slam (at weapon reach in front of you)")]
        public float SlamDamage = 3f;
        [Tooltip("Added to the slam: weapon damage * this")]
        public float SlamWeaponDamageMultiplier = 0.8f;
        public float SlamRadius = 1.5f;

        [Header("Quake (around the slam, later)")]
        [Tooltip("Seconds after the slam")]
        [Min(0f)] public float QuakeDelay = 1f;
        public float QuakeDamage = 2f;
        [Tooltip("Added to the quake: weapon damage * this")]
        public float QuakeWeaponDamageMultiplier = 0.6f;
        public float QuakeRadius = 4f;
        [Tooltip("How hard enemies are thrown outward")]
        public float QuakeHitForce = 8f;

        public override SkillType Type => SkillType.Upheaval;
        public override float ResourceCost => resourceCost;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(WeaponDamageLine(DamageType.Physical, "Slam damage", SlamDamage, SlamDamage, SlamWeaponDamageMultiplier, stats));
            description.Damage.Add(WeaponDamageLine(DamageType.Physical, "Quake damage", QuakeDamage, QuakeDamage, QuakeWeaponDamageMultiplier, stats));
            description.Effects.Add(AreaLine("Slam radius", SlamRadius, stats));
            description.Effects.Add(AreaLine("Quake radius", QuakeRadius, stats));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Duration), "Quake after", QuakeDelay, null, " s"));
            description.Effects.Add(AttackTimeLine("Time between slams", AttackSpeed, stats));
        }

        protected override Upheaval Configure(Upheaval component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            component.ResourceCost = resourceCost;
            component.SlamDamage = SlamDamage;
            component.SlamWeaponDamageMultiplier = SlamWeaponDamageMultiplier;
            component.SlamRadius = SlamRadius;
            component.QuakeDelay = QuakeDelay;
            component.QuakeDamage = QuakeDamage;
            component.QuakeWeaponDamageMultiplier = QuakeWeaponDamageMultiplier;
            component.QuakeRadius = QuakeRadius;
            component.QuakeHitForce = QuakeHitForce;
            return component;
        }
    }
}
