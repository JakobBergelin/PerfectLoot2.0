using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// A slow, wide weapon swing that builds resource: right to left, then left to right. Reaches twice the weapon reach and
    /// counts the area of effect bonus twice.
    /// </summary>
    [CreateAssetMenu(fileName = "Swipe", menuName = "Active Skills/Swipe")]
    public class SwipeSkill : ActiveSkill<Swipe>
    {
        [Tooltip("Seconds before the next swing (shared by all repeated attacks), scaled by the attack speed stat")]
        public float AttackSpeed = 0.9f;
        [Tooltip("Physical damage of each swing, before weapon damage")]
        public float Damage = 1f;
        [Tooltip("Added to each swing: weapon damage * this")]
        public float WeaponDamageMultiplier = 0.6f;
        [Tooltip("Radius of the blade's hit circle, before area of effect (counted twice)")]
        public float Radius = 0.8f;
        [Tooltip("Degrees to each side of facing the swing covers")]
        [Range(5f, 180f)] public float ArcAngle = 70f;
        [Tooltip("Seconds one swing takes to cross the arc")]
        [Min(0.01f)] public float SweepDuration = 0.2f;
        [Tooltip("Knockback of each hit")]
        public float HitForce = 2f;
        [Tooltip("Resource for every enemy a swing hits")]
        public float ResourcePerHit = 1f;

        public override SkillType Type => SkillType.Swipe;
        public override float ResourceCost => 0f;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(WeaponDamageLine(DamageType.Physical, "Damage per swing", Damage, Damage, WeaponDamageMultiplier, stats));
            description.Effects.Add(new SkillLine().Text($"+{Num(ResourcePerHit)} resource for each enemy hit"));
            description.Effects.Add(new SkillLine().Text("Reach: twice your weapon reach"));
            description.Effects.Add(new SkillLine().Text("Area of effect bonuses count twice"));
            description.Effects.Add(AttackTimeLine("Time between swings", AttackSpeed, stats));
        }

        protected override Swipe Configure(Swipe component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            component.Damage = Damage;
            component.WeaponDamageMultiplier = WeaponDamageMultiplier;
            component.Radius = Radius;
            component.ArcAngle = ArcAngle;
            component.SweepDuration = SweepDuration;
            component.HitForce = HitForce;
            component.ResourcePerHit = ResourcePerHit;
            return component;
        }
    }
}
