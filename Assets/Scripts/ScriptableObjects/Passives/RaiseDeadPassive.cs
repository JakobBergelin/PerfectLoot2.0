using UnityEngine;

namespace Passives
{
    /// <summary>Allies that die near the wearer get back up at full health, rising out of the ground.</summary>
    [CreateAssetMenu(fileName = "RaiseDead", menuName = "Passives/Raise Dead")]
    public class RaiseDeadPassive : PassiveSkill<RaiseDead>
    {
        [Tooltip("Meters around the wearer")]
        public float Radius = 8f;
        [Tooltip("Seconds a raised unit takes to rise out of the ground (it doesn't act meanwhile)")]
        public float RiseDuration = 1.5f;
        [Tooltip("Meters it sinks before rising")]
        public float RiseDepth = 2f;
        [Tooltip("Seconds a raised unit is Risen: it can't be raised again until this runs out")]
        public float RisenDuration = 180f;

        public override PassiveType Type => PassiveType.RaiseDead;

        protected override RaiseDead CreateComponent() => new RaiseDead
        {
            Radius = Radius,
            RiseDuration = RiseDuration,
            RiseDepth = RiseDepth,
            RisenDuration = RisenDuration,
        };
    }
}
