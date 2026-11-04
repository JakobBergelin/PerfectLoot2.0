using UnityEngine;

namespace Equipment
{
    /// <summary>Each hit leaves a stacking debuff on the enemy that makes the wearer's next hits on it deal more damage.</summary>
    [CreateAssetMenu(fileName = "Sunder", menuName = "Items/Legendary Powers/Sunder")]
    public class SunderPower : LegendaryPower<Sunder>
    {
        [Tooltip("Damage added to a hit for each Sundered stack on the target, after its resistances")]
        public float DamagePerStack = 0.2f;
        [Tooltip("Most Sundered stacks on one enemy")]
        public int MaxStacks = 50;
        [Tooltip("Seconds Sundered lasts after the last hit; then all stacks fall off")]
        public float StackDuration = 3f;

        protected override Sunder Configure(Sunder component, bool isNew)
        {
            component.DamagePerStack = DamagePerStack;
            component.MaxStacks = MaxStacks;
            component.StackDuration = StackDuration;
            return component;
        }
    }
}
