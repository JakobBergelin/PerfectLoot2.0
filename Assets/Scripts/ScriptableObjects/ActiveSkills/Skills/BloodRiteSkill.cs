using Equipment;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>Pay health for a burst of Strength and Agility.</summary>
    [CreateAssetMenu(fileName = "BloodRite", menuName = "Active Skills/Blood Rite")]
    public class BloodRiteSkill : ActiveSkill<BloodRite>
    {
        [Tooltip("Seconds before it can be used again, before cooldown reduction")]
        public float CooldownSeconds = 20f;
        [Tooltip("Share of max health paid (0.2 = 20%); never takes you below 1 health")]
        [Range(0f, 1f)] public float HealthCost = 0.2f;
        [Tooltip("Seconds the buff lasts, scaled by the buff duration stat")]
        public float Duration = 8f;
        public float StrengthBonus = 5f;
        public float AgilityBonus = 5f;

        public override SkillType Type => SkillType.BloodRite;
        public override float Cooldown => CooldownSeconds;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.Health)).Text($"Costs {Pct(HealthCost)} of your max health"));
            description.Effects.Add(new SkillLine().Icon(GameIcons.Affix(Affix.Strength)).Text($"+{Num(StrengthBonus)} Strength and +{Num(AgilityBonus)} Agility for {Num(Duration)} s"));
        }

        protected override BloodRite Configure(BloodRite component, bool isNew)
        {
            component.Cooldown = CooldownSeconds;
            component.HealthCost = HealthCost;
            component.Duration = Duration;
            component.StrengthBonus = StrengthBonus;
            component.AgilityBonus = AgilityBonus;
            if (isNew) component.Elapsed = CooldownSeconds;
            return component;
        }
    }
}
