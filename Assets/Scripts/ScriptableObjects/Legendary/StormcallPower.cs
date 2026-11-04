using UnityEngine;

namespace Equipment
{
    /// <summary>While the wearer channels a skill, lightning keeps striking random enemies nearby.</summary>
    [CreateAssetMenu(fileName = "Stormcall", menuName = "Items/Legendary Powers/Stormcall")]
    public class StormcallPower : LegendaryPower<Stormcall>
    {
        [Tooltip("Seconds of channeling between strikes")]
        public float Interval = 0.5f;
        [Tooltip("Meters from the wearer to the enemies it can strike")]
        public float Range = 8f;
        [Tooltip("Lightning damage per strike, before the wearer's damage %")]
        public float Damage = 3f;

        protected override Stormcall Configure(Stormcall component, bool isNew)
        {
            component.Interval = Interval;
            component.Range = Range;
            component.Damage = Damage;
            return component;
        }
    }
}
