using UnityEngine;

namespace Passives
{
    /// <summary>Fencer's class passive: critical hits take time off every skill's cooldown.</summary>
    [CreateAssetMenu(fileName = "Precision", menuName = "Passives/Class/Precision")]
    public class PrecisionPassive : PassiveSkill<Precision>
    {
        [Tooltip("Seconds taken off every cooldown, at most once per frame")]
        [Min(0f)] public float CooldownCut = 0.3f;

        public override PassiveType Type => PassiveType.Precision;

        protected override Precision CreateComponent() => new Precision { CooldownCut = CooldownCut };
    }
}
