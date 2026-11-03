using UnityEngine;

namespace Equipment
{
    /// <summary>When a frozen enemy dies, it bursts into ice shards that fly on the way its killing blow went.</summary>
    [CreateAssetMenu(fileName = "Shatter", menuName = "Items/Legendary Powers/Shatter")]
    public class ShatterPower : LegendaryPower<Shatter>
    {
        [Tooltip("Ice shards per frozen enemy that dies")]
        public int ShardCount = 3;
        [Tooltip("Degrees between neighboring shards")]
        public float SpreadAngle = 15f;
        [Tooltip("Cold damage per shard, before the wearer's damage %")]
        public float Damage = 3f;
        [Tooltip("Meters per second, before the wearer's projectile speed")]
        public float ProjectileSpeed = 15f;
        [Tooltip("Each shard's hit radius in meters, scaled by the wearer's area of effect")]
        public float Radius = 0.25f;
        [Tooltip("Meters each shard flies at most")]
        public float Range = 10f;

        protected override Shatter Configure(Shatter component, bool isNew)
        {
            component.ShardCount = ShardCount;
            component.SpreadAngle = SpreadAngle;
            component.Damage = Damage;
            component.ProjectileSpeed = ProjectileSpeed;
            component.Radius = Radius;
            component.Range = Range;
            return component;
        }
    }
}
