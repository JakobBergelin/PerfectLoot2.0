using UnityEngine;

namespace Equipment
{
    /// <summary>Every few casts that cost resource, one is free.</summary>
    [CreateAssetMenu(fileName = "Insight", menuName = "Items/Legendary Powers/Insight")]
    public class InsightPower : LegendaryPower<Insight>
    {
        [Tooltip("Every this many casts with a one-off cost, the last one is free. Costs per second are not covered")]
        [Min(1)] public int CastsNeeded = 5;

        protected override Insight Configure(Insight component, bool isNew)
        {
            component.CastsNeeded = CastsNeeded;
            return component;
        }
    }
}
