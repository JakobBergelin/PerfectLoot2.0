using Equipment;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// A thin line of cold thrust from the weapon's reach that builds resource, and briefly takes less damage from
    /// projectiles. Faster than Swipe, slower than Fists of Thunder.
    /// </summary>
    [CreateAssetMenu(fileName = "FrostLance", menuName = "Active Skills/Frost Lance")]
    public class FrostLanceSkill : ActiveSkill<FrostLance>
    {
        [Tooltip("Seconds before the next thrust (shared by all repeated attacks), scaled by the attack speed stat")]
        public float AttackSpeed = 0.55f;
        [Tooltip("Cold damage of each thrust: weapon damage * this")]
        public float WeaponDamageMultiplier = 0.5f;
        [Tooltip("Meters the line reaches past the weapon's reach, before area of effect")]
        [Min(0.1f)] public float Length = 10f;
        [Tooltip("Meters wide, before area of effect")]
        [Min(0.05f)] public float Width = 0.8f;
        [Tooltip("Meters high, so it reaches units of any size (not scaled)")]
        [Min(0.1f)] public float Height = 3f;
        [Tooltip("Knockback of each hit, sideways out of the line")]
        public float HitForce = 1f;
        [Tooltip("Resource gained by each thrust")]
        public float ResourcePerAttack = 1f;

        [Header("Buff on each thrust")]
        [Tooltip("Less damage from projectiles' hits while it lasts: 0.2 = -20%")]
        public float ProjectileDamageReduction = 0.2f;
        [Tooltip("Seconds, scaled by the buff duration stat")]
        public float BuffDuration = 1f;

        public override SkillType Type => SkillType.FrostLance;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Damage.Add(WeaponDamageLine(DamageType.Cold, "Damage per thrust", 0f, 0f, WeaponDamageMultiplier, stats));
            description.Effects.Add(new SkillLine().Text($"Reach: {Num(Length)} m past your weapon reach, {Num(Width)} m wide"));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.ResourceRegeneration)).Text($"Resource per thrust: +{Num(ResourcePerAttack)}"));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.ProjectileDamageReduction))
                .Text($"{Pct(ProjectileDamageReduction)} less damage from projectiles for {Num(BuffDuration)} s"));
            description.Effects.Add(AttackTimeLine("Time between thrusts", AttackSpeed, stats));
        }

        protected override FrostLance Configure(FrostLance component, bool isNew)
        {
            component.AttackSpeed = AttackSpeed;
            component.WeaponDamageMultiplier = WeaponDamageMultiplier;
            component.Length = Length;
            component.Width = Width;
            component.Height = Height;
            component.HitForce = HitForce;
            component.ResourcePerAttack = ResourcePerAttack;
            component.ProjectileDamageReduction = ProjectileDamageReduction;
            component.BuffDuration = BuffDuration;
            return component;
        }
    }
}
