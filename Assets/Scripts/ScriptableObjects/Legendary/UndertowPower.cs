using UnityEngine;

namespace Equipment
{
    /// <summary>The wearer's hits pull enemies toward where they landed instead of pushing them away.</summary>
    [CreateAssetMenu(fileName = "Undertow", menuName = "Items/Legendary Powers/Undertow")]
    public class UndertowPower : LegendaryPower<Undertow>
    {
        [Tooltip("Times the hit's knockback (2 = pulls twice as hard as it would have pushed)")]
        public float PullStrength = 2f;

        protected override Undertow Configure(Undertow component, bool isNew)
        {
            component.PullStrength = PullStrength;
            return component;
        }
    }
}
