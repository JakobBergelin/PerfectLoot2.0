using Equipment;
using Unity.Entities;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>A usable skill. One asset per SkillType, equipped in an ActiveSkillSlot through ActiveSkillManager.</summary>
    public abstract class ActiveSkill : ScriptableObject
    {
        public string Name;
        public Sprite Icon;
        [TextArea] public string Description;
        [Tooltip("What kind of skill it is: effects pick skills by tag, and Melee / Mobility uses raise their events")]
        public SkillTags Tags;

        public abstract SkillType Type { get; }

        // Shown in UI (copied into SkillState). Subclasses override the ones that apply to them.
        public virtual float Cooldown => 0f;
        public virtual float ResourceCost => 0f;
        public virtual float ResourceCostPerSecond => 0f;

        /// <summary>The cooldown for a caster with these stats (CooldownReduction); skills with more scaling override it, in step with their system.</summary>
        public virtual float CooldownFor(in StatBlock stats) => SkillUtil.Cooldown(Cooldown, stats);

        /// <summary>Adds the skill's component (disabled until its key is pressed), or refreshes its configured values.</summary>
        public abstract void Apply(EntityManager entityManager, Entity entity);

        public abstract void Remove(EntityManager entityManager, Entity entity);

        /// <summary>
        /// The skill's numbers for its tooltip (SkillTooltip): its damage and other effects, using the same formulas as
        /// its system, so keep the two in step. stats: the caster's current stats, or null without a character (then
        /// only the make-up is shown, no totals). The Damage % stat and crits aren't included.
        /// </summary>
        public virtual void Describe(SkillDescription description, StatBlock? stats) {}

        // ---------- Helpers for Describe ----------

        protected static string Num(float value) => value.ToString("0.##");

        protected static string Pct(float fraction) => (fraction * 100f).ToString("0.#") + "%";

        protected static string RangeText(float min, float max) => Mathf.Approximately(min, max) ? Num(min) : $"{Num(min)}–{Num(max)}";

        /// <summary>"[fire] label: " to continue with the amount. The icon is the element the caster's conversion turns type into.</summary>
        protected static SkillLine DamageLine(DamageType type, string label, StatBlock? stats) =>
            new SkillLine().Icon(GameIcons.DamageType(stats is StatBlock s ? s.ConvertedType(type) : type)).Text(label + ": ");

        /// <summary>
        /// Flat damage (flatMin to flatMax) plus weapon damage × weaponMultiplier: "14 (5 + 20% [sword])", a range when
        /// the weapon has one (WeaponDamage ± WeaponDamageRange).
        /// </summary>
        protected static SkillLine WeaponDamageLine(DamageType type, string label, float flatMin, float flatMax, float weaponMultiplier, StatBlock? stats)
        {
            var line = DamageLine(type, label, stats);
            if (stats is StatBlock s)
            {
                float low = flatMin + (s.WeaponDamage - s.WeaponDamageRange) * weaponMultiplier;
                float high = flatMax + (s.WeaponDamage + s.WeaponDamageRange) * weaponMultiplier;
                line.Text(RangeText(low, high) + " (");
            }
            if (flatMax > 0f) line.Text(RangeText(flatMin, flatMax) + " + ");
            line.Text(Pct(weaponMultiplier)).Icon(GameIcons.Affix(Affix.WeaponDamage));
            if (stats != null) line.Text(")");
            return line;
        }

        /// <summary>"[icon] label: 3.6 s (3 s)": the value with the caster's stats, base greyed when they differ.</summary>
        protected static SkillLine ValueLine(Sprite icon, string label, float baseValue, float? current, string unit = "")
        {
            var line = new SkillLine().Icon(icon).Text(label + ": ");
            if (current is float value && !Mathf.Approximately(value, baseValue))
                return line.Text($"{Num(value)}{unit} ").Muted($"({Num(baseValue)}{unit})");
            return line.Text(Num(baseValue) + unit);
        }

        /// <summary>A radius scaled by the area of effect stat (StatBlock.AreaMultiplier), in meters.</summary>
        protected static SkillLine AreaLine(string label, float radius, StatBlock? stats) =>
            ValueLine(GameIcons.Affix(Affix.AreaOfEffect), label, radius, stats is StatBlock s ? radius * s.AreaMultiplier : (float?)null, " m");

        /// <summary>A wind-up timed by attack speed: seconds / the AttackSpeed stat.</summary>
        protected static SkillLine WindUpLine(string label, float seconds, StatBlock? stats) =>
            ValueLine(GameIcons.Affix(Affix.AttackSpeed), label, seconds,
                      stats is StatBlock s && s.AttackSpeed > 0f ? seconds / s.AttackSpeed : (float?)null, " s");
    }

    /// <summary>A skill backed by one enableable ECS component.</summary>
    public abstract class ActiveSkill<T> : ActiveSkill where T : unmanaged, IComponentData, IEnableableComponent
    {
        /// <summary>Copies this asset's values into the component, keeping its runtime fields (timers etc.).</summary>
        protected abstract T Configure(T component, bool isNew);

        public override void Apply(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<T>(entity))
            {
                entityManager.SetComponentData(entity, Configure(entityManager.GetComponentData<T>(entity), isNew: false));
            }
            else
            {
                entityManager.AddComponentData(entity, Configure(default, isNew: true));
                entityManager.SetComponentEnabled<T>(entity, false);
            }
        }

        public override void Remove(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<T>(entity))
                entityManager.RemoveComponent<T>(entity);
        }
    }
}
