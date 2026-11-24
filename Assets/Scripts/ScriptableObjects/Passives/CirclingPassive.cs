using Unity.Mathematics;
using UnityEngine;

namespace Passives
{
    /// <summary>An enemy passive: it runs around its target instead of walking up to it (CirclingSystem).</summary>
    [CreateAssetMenu(fileName = "Circling", menuName = "Passives/Circling")]
    public class CirclingPassive : PassiveSkill<Circling>
    {
        [Tooltip("Meters from its target it runs around at")]
        public float Radius = 6f;
        [Tooltip("Degrees ahead along the circle it heads for: more cuts the corner more")]
        public float Lead = 40f;

        public override PassiveType Type => PassiveType.Circling;

        protected override Circling CreateComponent() => new Circling { Radius = Radius, Lead = math.radians(Lead) };
    }
}
