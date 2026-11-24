using UnityEngine;

namespace Passives
{
    /// <summary>An enemy passive: it leaves hazards behind as it moves, that hurt players and companions (PoisonTrailSystem).</summary>
    [CreateAssetMenu(fileName = "PoisonTrail", menuName = "Passives/Poison Trail")]
    public class PoisonTrailPassive : PassiveSkill<PoisonTrail>
    {
        public DamageType DamageType = DamageType.Poison;
        public float DamagePerSecond = 1f;
        [Tooltip("Meters")]
        public float Radius = 1.2f;
        [Tooltip("Seconds each hazard stays")]
        public float Lifetime = 5f;
        [Tooltip("Meters it moves between two hazards")]
        public float Spacing = 1.5f;

        public override PassiveType Type => PassiveType.PoisonTrail;

        protected override PoisonTrail CreateComponent() => new PoisonTrail
        {
            Type = DamageType, DamagePerSecond = DamagePerSecond, Radius = Radius, Lifetime = Lifetime, Spacing = Spacing,
        };
    }
}
