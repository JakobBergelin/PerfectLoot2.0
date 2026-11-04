using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "PoisonDart", menuName = "Active Skills/Poison Dart")]
    public class PoisonDartSkill : ActiveSkill<PoisonDart>
    {
        [Tooltip("Seconds before the next attack after each dart (shared by all repeated attacks), scaled by the attack speed stat")]
        public float AttackSpeed = 0.5f;
        [Tooltip("A dart's poison damage, as a share of weapon damage")]
        public float WeaponDamageMultiplier = 0.25f;
        public float ProjectileSpeed = 15f;
        [Tooltip("Hit radius of the dart")]
        public float Radius = 0.25f;
        [Tooltip("Knockback speed on hit, away from the dart")]
        public float HitForce = 2f;
        [Tooltip("Seconds a hit marks the enemy; another hit starts them over")]
        public float MarkDuration = 5f;
        [Tooltip("Poison damage when a marked enemy dies, not based on weapon damage")]
        public float ExplosionDamage = 15f;
        [Tooltip("Meters around the dying enemy, scaled by the caster's area of effect")]
        public float ExplosionRadius = 4f;

        public override SkillType Type => SkillType.PoisonDart;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(WeaponDamageLine(DamageType.Poison, "Dart damage", 0f, 0f, WeaponDamageMultiplier, stats));
            description.Damage.Add(DamageLine(DamageType.Poison, "Explosion damage", stats).Text(Num(ExplosionDamage)));
            description.Effects.Add(AreaLine("Explosion radius", ExplosionRadius, stats));
            description.Effects.Add(new SkillLine().Text($"Marks the enemy hit for {Num(MarkDuration)} s"));
            description.Effects.Add(AttackTimeLine("Time between darts", AttackSpeed, stats));
        }

        protected override PoisonDart Configure(PoisonDart component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            component.WeaponDamageMultiplier = WeaponDamageMultiplier;
            component.ProjectileSpeed = ProjectileSpeed;
            component.Radius = Radius;
            component.HitForce = HitForce;
            component.MarkDuration = MarkDuration;
            component.ExplosionDamage = ExplosionDamage;
            component.ExplosionRadius = ExplosionRadius;
            return component;
        }
    }
}
