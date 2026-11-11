using UnityEngine;

namespace Passives
{
    /// <summary>Chills everyone on the other side near the wearer, a few stacks at a time; standing in it long enough freezes.</summary>
    [CreateAssetMenu(fileName = "FrostAura", menuName = "Passives/Frost Aura")]
    public class FrostAuraPassive : PassiveSkill<FrostAura>
    {
        [Tooltip("Meters around the wearer")]
        public float Radius = 6f;
        [Tooltip("Chill stacks each tick")]
        [Min(0)] public int ChillPerTick = 5;
        [Tooltip("Seconds between ticks")]
        [Min(0.05f)] public float TickRate = 1f;

        public override PassiveType Type => PassiveType.FrostAura;

        protected override FrostAura CreateComponent() => new FrostAura
        {
            Radius = Radius,
            ChillPerTick = ChillPerTick,
            TickRate = TickRate,
        };
    }
}
