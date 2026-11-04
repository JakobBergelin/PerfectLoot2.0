using UnityEngine;

namespace Passives
{
    /// <summary>Enemy deaths give stacking attack speed for a few seconds.</summary>
    [CreateAssetMenu(fileName = "Bloodrush", menuName = "Passives/Bloodrush")]
    public class BloodrushPassive : PassiveSkill<Bloodrush>
    {
        [Tooltip("Attack speed for each stack (0.03 = +3%)")]
        public float AttackSpeedPerStack = 0.03f;
        [Tooltip("Most stacks at once")]
        [Min(1)] public int MaxStacks = 10;
        [Tooltip("Seconds the buff lasts after the last death; then all stacks fall off")]
        public float Duration = 4f;

        public override PassiveType Type => PassiveType.Bloodrush;

        protected override Bloodrush CreateComponent() => new Bloodrush
        {
            AttackSpeedPerStack = AttackSpeedPerStack,
            MaxStacks = MaxStacks,
            Duration = Duration,
        };
    }
}
