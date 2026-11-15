using UnityEngine;

namespace Equipment
{
    /// <summary>The wearer's tornadoes head for its aim point and circle it instead of wandering.</summary>
    [CreateAssetMenu(fileName = "TamedWinds", menuName = "Items/Legendary Powers/Tamed Winds")]
    public class TamedWindsPower : LegendaryPower<TamedWinds>
    {
        [Tooltip("Meters from the aim point that the tornadoes circle at")]
        [Min(0f)] public float CircleRadius = 2f;
        [Tooltip("How hard a tornado is steered back to its circle, per meter off it")]
        [Min(0f)] public float PullStrength = 1f;
        [Tooltip("How fast a tornado turns toward where it's steered (per second)")]
        [Min(0f)] public float TurnRate = 6f;

        protected override TamedWinds Configure(TamedWinds component, bool isNew)
        {
            component.CircleRadius = CircleRadius;
            component.PullStrength = PullStrength;
            component.TurnRate = TurnRate;
            return component;
        }
    }
}
