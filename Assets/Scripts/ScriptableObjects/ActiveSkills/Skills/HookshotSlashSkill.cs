using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "HookshotSlash", menuName = "Active Skills/Hookshot Slash")]
    public class HookshotSlashSkill : ActiveSkill<HookshotSlash>
    {
        [SerializeField] float resourceCost = 8f;
        [Tooltip("Furthest the hook flies, in meters")]
        public float Range = 12f;
        [Tooltip("Meters per second the hook flies")]
        public float HookSpeed = 40f;
        [Tooltip("Meters per second the caster is pulled to the hook")]
        public float PullSpeed = 30f;

        [Header("Slash")]
        [Tooltip("Physical damage to each enemy passed = weapon damage * this")]
        public float WeaponDamageMultiplier = 1.5f;
        [Tooltip("Hit radius around the caster while pulled, scaled by the area of effect stat")]
        public float Radius = 1f;
        [Tooltip("Knockback speed on hit, away from the caster")]
        public float HitForce = 6f;

        public override SkillType Type => SkillType.HookshotSlash;
        public override float ResourceCost => resourceCost;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(WeaponDamageLine(DamageType.Physical, "Damage per enemy passed", 0f, 0f, WeaponDamageMultiplier, stats));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Distance), "Range", Range, null, " m"));
            description.Effects.Add(AreaLine("Hit radius", Radius, stats));
        }

        protected override HookshotSlash Configure(HookshotSlash component, bool isNew)
        {
            component.ResourceCost = resourceCost;
            component.Range = Range;
            component.HookSpeed = HookSpeed;
            component.PullSpeed = PullSpeed;
            component.WeaponDamageMultiplier = WeaponDamageMultiplier;
            component.Radius = Radius;
            component.HitForce = HitForce;
            return component;
        }
    }
}
