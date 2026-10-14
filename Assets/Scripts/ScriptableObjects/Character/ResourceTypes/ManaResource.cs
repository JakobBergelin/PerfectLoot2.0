using UnityEngine;

namespace Characters
{
    [CreateAssetMenu(fileName = "Mana", menuName = "Character/Resource/Mana")]
    public class ManaResource : ResourceDefinition<Mana>
    {
        public override ResourceType Type => ResourceType.Mana;

        protected override Mana CreateComponent() => new Mana { MaxResource = MaxResource, Regeneration = Regeneration };
    }
}
