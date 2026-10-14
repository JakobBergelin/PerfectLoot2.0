using UnityEngine;

namespace Characters
{
    [CreateAssetMenu(fileName = "Arcane", menuName = "Character/Resource/Arcane")]
    public class ArcaneResource : ResourceDefinition<Arcane>
    {
        public override ResourceType Type => ResourceType.Arcane;

        protected override Arcane CreateComponent() => new Arcane { MaxResource = MaxResource, Regeneration = Regeneration };
    }
}
