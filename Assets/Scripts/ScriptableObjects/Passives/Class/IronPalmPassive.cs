using UnityEngine;

namespace Passives
{
    /// <summary>Monk's class passive: dodged and blocked hits store Ki; enough Ki sends healing orbs to you and your allies.</summary>
    [CreateAssetMenu(fileName = "IronPalm", menuName = "Passives/Class/Iron Palm")]
    public class IronPalmPassive : PassiveSkill<IronPalm>
    {
        [Tooltip("Dodged or blocked hits needed")]
        [Min(1)] public int KiNeeded = 3;
        [Tooltip("Heal of each orb, as a share of its target's max health (0.1 = 10%)")]
        [Range(0f, 1f)] public float HealShare = 0.1f;
        [Tooltip("Meters around you that allies get an orb within")]
        [Min(0f)] public float Range = 15f;
        [Tooltip("Orb speed, meters per second")]
        [Min(1f)] public float ProjectileSpeed = 9f;

        public override PassiveType Type => PassiveType.IronPalm;

        protected override IronPalm CreateComponent() => new IronPalm { KiNeeded = KiNeeded, HealShare = HealShare, Range = Range, ProjectileSpeed = ProjectileSpeed };
    }
}
