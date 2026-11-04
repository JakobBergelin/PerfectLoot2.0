using Unity.Entities;
using UnityEngine;

namespace Equipment
{
    /// <summary>Moving builds up damage; stopping loses it all.</summary>
    [CreateAssetMenu(fileName = "Momentum", menuName = "Items/Legendary Powers/Momentum")]
    public class MomentumPower : LegendaryPower<Momentum>
    {
        [Tooltip("Damage for each stack (0.03 = +3%)")]
        public float DamagePerStack = 0.03f;
        [Tooltip("Most stacks at once")]
        [Min(1)] public int MaxStacks = 10;
        [Tooltip("Seconds of moving for each stack")]
        public float SecondsPerStack = 1f;

        protected override Momentum Configure(Momentum component, bool isNew)
        {
            component.DamagePerStack = DamagePerStack;
            component.MaxStacks = MaxStacks;
            component.SecondsPerStack = SecondsPerStack;
            return component;
        }

        // Without the power nothing turns the buff off anymore
        public override void Remove(EntityManager entityManager, Entity entity)
        {
            base.Remove(entityManager, entity);
            if (entityManager.HasComponent<MomentumBuff>(entity))
                entityManager.SetComponentEnabled<MomentumBuff>(entity, false);
        }
    }
}
