using Equipment;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// Switches between three stances, Strength, Agility and Intelligence, each press. The stance gives a bonus to its
    /// attribute, so skills' requirements can be met during a fight. Remembered through deaths and scenes.
    /// </summary>
    [CreateAssetMenu(fileName = "Stances", menuName = "Active Skills/Stances")]
    public class StancesSkill : ActiveSkill<Stances>
    {
        [Tooltip("Seconds between switches")]
        [Min(0f)] public float SwitchCooldown = 0.5f;
        [Tooltip("Added to the current stance's attribute")]
        public float Bonus = 5f;

        public override SkillType Type => SkillType.Stances;
        public override float Cooldown => SwitchCooldown;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Effects.Add(new SkillLine().Text($"+{Num(Bonus)} to the stance's attribute: Strength, Agility or Intelligence"));
            description.Effects.Add(new SkillLine().Text("Press to switch to the next stance"));
        }

        // Cooldown doesn't shorten with cooldown reduction: it's only the pause between switches
        public override float CooldownFor(in StatBlock stats) => SwitchCooldown;

        protected override Stances Configure(Stances component, bool isNew)
        {
            component.Cooldown = SwitchCooldown;
            component.Bonus = Bonus;
            if (isNew) component.Elapsed = SwitchCooldown;
            return component;
        }
    }
}
