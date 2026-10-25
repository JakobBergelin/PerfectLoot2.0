using UnityEngine;

namespace Equipment
{
    /// <summary>
    /// While the resource is nearly full, melee attacks (Fists of Thunder punches, Whirlwind ticks) also cast a random
    /// equipped projectile skill toward the cursor, which still costs its resource.
    /// </summary>
    [CreateAssetMenu(fileName = "EchoingBlade", menuName = "Items/Legendary Powers/Echoing Blade")]
    public class EchoingBladePower : LegendaryPower<EchoingBlade>
    {
        [Tooltip("How full the resource must be (0-1) when the melee attack lands")]
        [Range(0f, 1f)] public float ResourceThreshold = 0.95f;

        protected override EchoingBlade Configure(EchoingBlade component, bool isNew)
        {
            component.ResourceThreshold = ResourceThreshold;
            return component;
        }
    }
}
