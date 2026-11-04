using UnityEngine;

namespace Equipment
{
    /// <summary>Survives a killing blow with a sliver of health, on a long cooldown.</summary>
    [CreateAssetMenu(fileName = "LastStand", menuName = "Items/Legendary Powers/Last Stand")]
    public class LastStandPower : LegendaryPower<LastStand>
    {
        [Tooltip("Seconds before it can save the wearer again. Cooldown reduction doesn't shorten it")]
        public float Cooldown = 60f;
        [Tooltip("Health the wearer is left with")]
        [Min(1f)] public float HealthLeft = 1f;

        protected override LastStand Configure(LastStand component, bool isNew)
        {
            component.Cooldown = Cooldown;
            component.HealthLeft = HealthLeft;
            if (isNew) component.Elapsed = Cooldown;  // Ready as soon as it's equipped
            return component;
        }
    }
}
