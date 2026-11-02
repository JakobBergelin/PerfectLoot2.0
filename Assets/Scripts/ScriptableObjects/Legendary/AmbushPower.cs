using UnityEngine;

namespace Equipment
{
    /// <summary>Hits on enemies that are still at full health always critically strike. Strong openers against fresh packs.</summary>
    [CreateAssetMenu(fileName = "Ambush", menuName = "Items/Legendary Powers/Ambush")]
    public class AmbushPower : LegendaryPower<Ambush>
    {
        [Tooltip("Hits on targets at or above this share of their max health always crit (1 = full health)")]
        [Range(0f, 1f)] public float HealthThreshold = 1f;

        protected override Ambush Configure(Ambush component, bool isNew)
        {
            component.HealthThreshold = HealthThreshold;
            return component;
        }
    }
}
