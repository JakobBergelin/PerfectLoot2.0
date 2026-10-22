using UnityEngine;

namespace Equipment
{
    /// <summary>Using a mobility skill (e.g. Dash) grants Refraction stacks, each absorbing one hit.</summary>
    [CreateAssetMenu(fileName = "Refractionist", menuName = "Items/Legendary Powers/Refractionist")]
    public class RefractionistPower : LegendaryPower<Refractionist>
    {
        [Tooltip("Refraction stacks gained per mobility skill use")]
        public int RefractionStacks = 3;
        [Tooltip("Most Refraction stacks the wearer can have from this")]
        public int MaxStacks = 10;

        protected override Refractionist Configure(Refractionist component, bool isNew)
        {
            component.RefractionStacks = RefractionStacks;
            component.MaxStacks = MaxStacks;
            return component;
        }
    }
}
