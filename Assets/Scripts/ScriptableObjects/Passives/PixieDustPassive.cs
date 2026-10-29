using UnityEngine;

namespace Passives
{
    /// <summary>While Invulnerable, health and resource regenerate faster.</summary>
    [CreateAssetMenu(fileName = "PixieDust", menuName = "Passives/Pixie Dust")]
    public class PixieDustPassive : PassiveSkill<PixieDust>
    {
        [Tooltip("Added to the health and resource regeneration multipliers while Invulnerable, 2 = +200% (3x regeneration)")]
        public float RegenerationMultiplier = 2f;

        public override PassiveType Type => PassiveType.PixieDust;

        protected override PixieDust CreateComponent() => new PixieDust { RegenerationMultiplier = RegenerationMultiplier };
    }
}
