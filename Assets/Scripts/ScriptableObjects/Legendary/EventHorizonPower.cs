using UnityEngine;

namespace Equipment
{
    /// <summary>The wearer's projectiles orbit it, up to MaxProjectiles; each new one pushes the oldest out of orbit.</summary>
    [CreateAssetMenu(fileName = "EventHorizon", menuName = "Items/Legendary Powers/Event Horizon")]
    public class EventHorizonPower : LegendaryPower<EventHorizon>
    {
        [Tooltip("Most projectiles in orbit at once; a new one releases the oldest")]
        [Min(1)] public int MaxProjectiles = 10;
        [Tooltip("Orbit radius per unit of projectile speed (seconds): faster projectiles orbit further out")]
        [Min(0f)] public float RadiusPerSpeed = 0.25f;
        [Min(0f)] public float MinRadius = 2f;
        [Min(0f)] public float MaxRadius = 8f;
        [Tooltip("How hard a projectile is steered toward its orbit radius")]
        [Min(0f)] public float PullStrength = 2f;
        [Tooltip("Radians per second the orbit can turn a projectile. Must be faster than the orbit itself (1 / Radius Per Speed).")]
        [Min(0f)] public float TurnSpeed = 12f;
        [Tooltip("Spreads the projectiles evenly around the orbit: one half a turn from its place orbits this much further out (ahead) or in (behind), so it falls back or catches up. 0: no spreading.")]
        [Range(0f, 0.5f)] public float SpreadStrength = 0.5f;

        protected override EventHorizon Configure(EventHorizon component, bool isNew)
        {
            component.MaxProjectiles = MaxProjectiles;
            component.RadiusPerSpeed = RadiusPerSpeed;
            component.MinRadius = MinRadius;
            component.MaxRadius = Mathf.Max(MinRadius, MaxRadius);
            component.PullStrength = PullStrength;
            component.TurnSpeed = TurnSpeed;
            component.SpreadStrength = SpreadStrength;
            return component;
        }
    }
}
