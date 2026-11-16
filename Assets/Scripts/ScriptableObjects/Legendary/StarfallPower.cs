using UnityEngine;

namespace Equipment
{
    /// <summary>Meteor becomes a channel: while held, small meteors keep falling around the aim point.</summary>
    [CreateAssetMenu(fileName = "Starfall", menuName = "Items/Legendary Powers/Starfall")]
    public class StarfallPower : LegendaryPower<Starfall>
    {
        [Tooltip("Resource drained per second while channeling (changed by channel cost effects such as Afterburner)")]
        [Min(0f)] public float ResourceCostPerSecond = 25f;
        [Tooltip("Seconds between meteors, before the attack speed stat")]
        [Min(0.01f)] public float Interval = 0.33f;
        [Tooltip("Meteors land at a random spot within this many meters of the aim point")]
        [Min(0f)] public float Spread = 3f;
        [Tooltip("Seconds from a meteor's call to its impact")]
        [Min(0f)] public float Delay = 1f;
        [Tooltip("Share of Meteor's impact and burning ground damage each small meteor deals (0.35 = 35%)")]
        [Min(0f)] public float DamageShare = 0.35f;
        [Tooltip("Share of Meteor's impact and burning ground radius each small meteor has (0.5 = 50%)")]
        [Min(0f)] public float RadiusShare = 0.5f;
        [Tooltip("Seconds each small meteor's burning ground lasts")]
        [Min(0f)] public float HazardDuration = 2f;
        [Tooltip("Movement speed lost while channeling (0.4 = 40%)")]
        [Min(0f)] public float Slow = 0.4f;

        protected override Starfall Configure(Starfall component, bool isNew)
        {
            component.ResourceCostPerSecond = ResourceCostPerSecond;
            component.Interval = Interval;
            component.Spread = Spread;
            component.Delay = Delay;
            component.DamageShare = DamageShare;
            component.RadiusShare = RadiusShare;
            component.HazardDuration = HazardDuration;
            component.Slow = Slow;
            return component;
        }
    }
}
