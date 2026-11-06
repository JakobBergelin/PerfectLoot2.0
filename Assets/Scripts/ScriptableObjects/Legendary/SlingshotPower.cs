using UnityEngine;

namespace Equipment
{
    /// <summary>The wearer's projectiles split in two when they pass through another player or a companion.</summary>
    [CreateAssetMenu(fileName = "Slingshot", menuName = "Items/Legendary Powers/Slingshot")]
    public class SlingshotPower : LegendaryPower<Slingshot>
    {
        [Tooltip("Degrees between the two shots")]
        public float SpreadAngle = 40f;
        [Tooltip("Meters from an ally's center a shot splits at, added to the shot's hit radius")]
        public float AllyRadius = 1f;
        [Tooltip("Meters a shot flies before it can split, so a companion doesn't split its own shots")]
        public float MinFlight = 2f;

        protected override Slingshot Configure(Slingshot component, bool isNew)
        {
            component.SpreadAngle = SpreadAngle;
            component.AllyRadius = AllyRadius;
            component.MinFlight = MinFlight;
            return component;
        }
    }
}
