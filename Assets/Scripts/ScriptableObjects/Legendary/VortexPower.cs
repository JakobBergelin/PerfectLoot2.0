using UnityEngine;

namespace Equipment
{
    /// <summary>While Whirlwind spins, nearby enemies are pulled toward the wearer.</summary>
    [CreateAssetMenu(fileName = "Vortex", menuName = "Items/Legendary Powers/Vortex")]
    public class VortexPower : LegendaryPower<Vortex>
    {
        [Tooltip("Meters around the wearer that enemies are pulled from, times the Area of Effect multiplier")]
        [Min(0f)] public float PullRadius = 6f;
        [Tooltip("Meters per second toward the wearer")]
        [Min(0f)] public float PullSpeed = 3f;
        [Tooltip("Enemies this close aren't pulled further")]
        [Min(0f)] public float StopDistance = 1.5f;

        protected override Vortex Configure(Vortex component, bool isNew)
        {
            component.PullRadius = PullRadius;
            component.PullSpeed = PullSpeed;
            component.StopDistance = StopDistance;
            return component;
        }
    }
}
