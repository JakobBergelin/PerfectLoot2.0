using UnityEngine;

namespace Characters
{
    /// <summary>Regenerates faster while standing still or channeling.</summary>
    [CreateAssetMenu(fileName = "Focus", menuName = "Character/Resource/Focus")]
    public class FocusResource : ResourceDefinition<Focus>
    {
        // Added to the regeneration multiplier, 0.2 = +20%
        public float StandingStillRegenerationMultiplier = 0.2f;

        public override ResourceType Type => ResourceType.Focus;

        protected override Focus CreateComponent() => new Focus
        {
            MaxResource = MaxResource,
            Regeneration = Regeneration,
            StandingStillRegenerationMultiplier = StandingStillRegenerationMultiplier
        };
    }
}
