using UnityEngine;

namespace Passives
{
    /// <summary>Cleric's class passive: healing past full health, on anyone, becomes a Barrier on them.</summary>
    [CreateAssetMenu(fileName = "Overflow", menuName = "Passives/Class/Overflow")]
    public class OverflowPassive : PassiveSkill<Overflow>
    {
        [Tooltip("Largest barrier it gives, as a share of the healed one's max health (0.2 = 20%)")]
        [Range(0f, 1f)] public float MaxShare = 0.2f;
        [Tooltip("Seconds the barrier lasts after its last top-up")]
        [Min(0.1f)] public float Duration = 5f;

        public override PassiveType Type => PassiveType.Overflow;

        protected override Overflow CreateComponent() => new Overflow { MaxShare = MaxShare, Duration = Duration };
    }
}
