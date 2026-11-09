using UnityEngine;

namespace Equipment
{
    /// <summary>Enemies your melee hits leave nearly dead are finished off.</summary>
    [CreateAssetMenu(fileName = "Execute", menuName = "Items/Legendary Powers/Execute")]
    public class ExecutePower : LegendaryPower<Execute>
    {
        [Tooltip("An enemy one of your melee hits leaves below this much health dies")]
        public float HealthThreshold = 3f;

        protected override Execute Configure(Execute component, bool isNew)
        {
            component.HealthThreshold = HealthThreshold;
            return component;
        }
    }
}
