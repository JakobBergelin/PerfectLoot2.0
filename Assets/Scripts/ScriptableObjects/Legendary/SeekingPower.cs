using UnityEngine;

namespace Equipment
{
    /// <summary>Your projectiles home in on the enemy nearest where you aim.</summary>
    [CreateAssetMenu(fileName = "Seeking", menuName = "Items/Legendary Powers/Seeking")]
    public class SeekingPower : LegendaryPower<Seeking>
    {
        [Tooltip("Meters around the cursor to look for the enemy a projectile locks on to")]
        public float CursorRadius = 4f;
        [Tooltip("Radians per second the projectile turns at first (Barrage's missiles: 1.5)")]
        public float TurnSpeed = 6f;
        [Tooltip("Radians per second added to the turn speed every second it flies")]
        public float TurnAcceleration = 12f;

        protected override Seeking Configure(Seeking component, bool isNew)
        {
            component.CursorRadius = CursorRadius;
            component.TurnSpeed = TurnSpeed;
            component.TurnAcceleration = TurnAcceleration;
            return component;
        }
    }
}
