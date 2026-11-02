using UnityEngine;

namespace Equipment
{
    /// <summary>Zaps the nearest enemy every so often, and each zap lowers its lightning resistance further.</summary>
    [CreateAssetMenu(fileName = "StaticField", menuName = "Items/Legendary Powers/Static Field")]
    public class StaticFieldPower : LegendaryPower<StaticField>
    {
        [Tooltip("Seconds between zaps")]
        public float Interval = 0.5f;
        [Tooltip("Meters from the wearer to the enemy it zaps")]
        public float Range = 5f;
        [Tooltip("Lightning damage per zap, before the wearer's damage %")]
        public float Damage = 1f;
        [Tooltip("Lightning resistance each Conductive stack takes away")]
        public float ResistancePerStack = 5f;
        [Tooltip("Most Conductive stacks on one enemy")]
        public int MaxStacks = 30;
        [Tooltip("Seconds Conductive lasts after the last zap; then all stacks fall off")]
        public float StackDuration = 3f;

        protected override StaticField Configure(StaticField component, bool isNew)
        {
            component.Interval = Interval;
            component.Range = Range;
            component.Damage = Damage;
            component.ResistancePerStack = ResistancePerStack;
            component.MaxStacks = MaxStacks;
            component.StackDuration = StackDuration;
            return component;
        }
    }
}
