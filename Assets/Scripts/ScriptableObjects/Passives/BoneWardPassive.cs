using UnityEngine;

namespace Passives
{
    /// <summary>Raises the Cold resistance of allies near the wearer, itself included. Several wearers stack.</summary>
    [CreateAssetMenu(fileName = "BoneWard", menuName = "Passives/Bone Ward")]
    public class BoneWardPassive : PassiveSkill<BoneWard>
    {
        [Tooltip("Meters from the wearer")]
        public float Radius = 8f;
        [Tooltip("Added to Cold resistance by each wearer")]
        public float ColdResistance = 5f;

        public override PassiveType Type => PassiveType.BoneWard;

        protected override BoneWard CreateComponent() => new BoneWard
        {
            Radius = Radius,
            ColdResistance = ColdResistance,
        };
    }
}
