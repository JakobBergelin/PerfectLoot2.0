using UnityEngine;

namespace ActiveSkills
{
    [CreateAssetMenu(fileName = "Timezone", menuName = "Active Skills/Timezone")]
    public class TimezoneSkill : ActiveSkill<Timezone>
    {
        [SerializeField] float cooldown = 20f;
        [Tooltip("Seconds the field lasts")]
        public float Duration = 10f;
        [Tooltip("Meters around where it was cast, scaled by the caster's area of effect")]
        public float Radius = 8f;
        [Tooltip("Chill stacks per second for enemies in the field; at the max stacks they freeze")]
        public int ChillPerSecond = 20;
        [Tooltip("Fraction of their speed that projectiles in the field lose, 0.8 = 80% slower")]
        [Range(0f, 1f)] public float ProjectileSlow = 0.8f;

        public override SkillType Type => SkillType.Timezone;
        public override float Cooldown => cooldown;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Duration), "Field for", Duration, null, " s"));
            description.Effects.Add(AreaLine("Radius", Radius, stats));
            description.Effects.Add(new SkillLine().Text($"Chill: {ChillPerSecond} stacks per second"));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Equipment.Affix.ProjectileSpeed)).Text("Projectile speed: -" + Pct(ProjectileSlow)));
        }

        protected override Timezone Configure(Timezone component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.Duration = Duration;
            component.Radius = Radius;
            component.ChillPerTick = Mathf.RoundToInt(ChillPerSecond * TimezoneFieldSystem.TickInterval);
            component.ProjectileSlow = ProjectileSlow;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }
    }
}
