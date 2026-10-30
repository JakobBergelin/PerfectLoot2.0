using UnityEngine;

namespace Equipment
{
    /// <summary>Every skill hit and area of the wearer deals one element instead, its status effect included.</summary>
    [CreateAssetMenu(fileName = "ElementalConversion", menuName = "Items/Legendary Powers/Elemental Conversion")]
    public class ElementalConversionPower : LegendaryPower<ElementalConversion>
    {
        [Tooltip("The damage type all of the wearer's skills deal")]
        public DamageType Type = DamageType.Fire;

        protected override ElementalConversion Configure(ElementalConversion component, bool isNew)
        {
            component.Type = Type;
            return component;
        }
    }
}
