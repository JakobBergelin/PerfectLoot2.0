using UnityEngine;

namespace Characters
{
    /// <summary>Gains resource on every main hit taken.</summary>
    [CreateAssetMenu(fileName = "Rage", menuName = "Character/Resource/Rage")]
    public class RageResource : ResourceDefinition<Rage>
    {
        public float ResourcePerHit = 3f;

        public override ResourceType Type => ResourceType.Rage;

        protected override Rage CreateComponent() => new Rage { MaxResource = MaxResource, Regeneration = Regeneration, ResourcePerHit = ResourcePerHit };
    }
}
