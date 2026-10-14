using UnityEngine;

namespace Characters
{
    /// <summary>Gains resource whenever a skill is used.</summary>
    [CreateAssetMenu(fileName = "Energy", menuName = "Character/Resource/Energy")]
    public class EnergyResource : ResourceDefinition<Energy>
    {
        public float ResourceOnSkillUse = 2f;

        public override ResourceType Type => ResourceType.Energy;

        protected override Energy CreateComponent() => new Energy { MaxResource = MaxResource, Regeneration = Regeneration, ResourceOnSkillUse = ResourceOnSkillUse };
    }
}
