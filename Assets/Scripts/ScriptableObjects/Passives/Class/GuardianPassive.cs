using UnityEngine;

namespace Passives
{
    /// <summary>Paladin's class passive: takes a share of nearby allies' damage, and what it takes builds up Consecration.</summary>
    [CreateAssetMenu(fileName = "Guardian", menuName = "Passives/Class/Guardian")]
    public class GuardianPassive : PassiveSkill<Guardian>
    {
        [Tooltip("Meters around you that allies must be within")]
        [Min(0f)] public float Radius = 6f;
        [Tooltip("Share of a nearby ally's damage you take instead (0.25 = 25%)")]
        [Range(0f, 1f)] public float Share = 0.25f;
        [Tooltip("Armor for each ally within Radius")]
        [Min(0)] public int ArmorPerAlly = 2;

        [Header("Consecration")]
        [Tooltip("Damage taken (your own and shared) that releases it")]
        [Min(1f)] public float Threshold = 100f;
        [Tooltip("Meters around you it heals and hits")]
        [Min(0f)] public float ConsecrationRadius = 5f;
        [Tooltip("Physical damage to each enemy within range")]
        [Min(0f)] public float Damage = 5f;
        [Tooltip("Heal for each ally within range, as a share of your max health (0.1 = 10%)")]
        [Range(0f, 1f)] public float HealShare = 0.1f;

        public override PassiveType Type => PassiveType.Guardian;

        protected override Guardian CreateComponent() => new Guardian
        {
            Radius = Radius,
            Share = Share,
            ArmorPerAlly = ArmorPerAlly,
            ConsecrationThreshold = Threshold,
            ConsecrationRadius = ConsecrationRadius,
            ConsecrationDamage = Damage,
            ConsecrationHealShare = HealShare,
        };
    }
}
