using UnityEngine;

namespace Equipment
{
    /// <summary>Skills that cost little can always be cast, even without enough resource; casting one empties what the wearer has.</summary>
    [CreateAssetMenu(fileName = "Bottomless", menuName = "Items/Legendary Powers/Bottomless")]
    public class BottomlessPower : LegendaryPower<Bottomless>
    {
        [Tooltip("One-off skill costs up to this (after resource cost reduction) can always be paid. Costs per second are not covered")]
        public float MaxCost = 5f;

        protected override Bottomless Configure(Bottomless component, bool isNew)
        {
            component.MaxCost = MaxCost;
            return component;
        }
    }
}
