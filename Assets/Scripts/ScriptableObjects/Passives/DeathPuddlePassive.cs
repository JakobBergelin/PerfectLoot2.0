using UnityEngine;

namespace Passives
{
    /// <summary>An enemy passive: when the wearer dies it leaves a puddle that hurts players and companions (DeathPuddleSystem).</summary>
    [CreateAssetMenu(fileName = "DeathPuddle", menuName = "Passives/Death Puddle")]
    public class DeathPuddlePassive : PassiveSkill<DeathPuddle>
    {
        public DamageType DamageType = DamageType.Poison;
        public float DamagePerSecond = 1f;
        [Tooltip("Meters")]
        public float Radius = 2.5f;
        [Tooltip("Seconds it stays")]
        public float Lifetime = 3f;

        public override PassiveType Type => PassiveType.DeathPuddle;

        protected override DeathPuddle CreateComponent() => new DeathPuddle
        {
            Type = DamageType, DamagePerSecond = DamagePerSecond, Radius = Radius, Lifetime = Lifetime,
        };
    }
}
