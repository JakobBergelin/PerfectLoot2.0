using UnityEngine;

namespace Characters
{
    /// <summary>Gains resource on every main hit taken.</summary>
    [CreateAssetMenu(fileName = "Rage", menuName = "Character/Resource/Rage")]
    public class RageResource : ResourceDefinition<Rage>
    {
        public float ResourcePerHit = 3f;

        public override ResourceType Type => ResourceType.Rage;
        public override string Rule => $"+{Number(ResourcePerHit)} for each hit you take";

        protected override Rage CreateComponent() => new Rage { MaxResource = MaxResource, Regeneration = Regeneration, ResourcePerHit = ResourcePerHit };
    }
}
