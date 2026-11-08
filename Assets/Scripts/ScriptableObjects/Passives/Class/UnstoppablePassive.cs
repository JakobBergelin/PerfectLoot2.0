using UnityEngine;

namespace Passives
{
    /// <summary>Warrior's class passive: surrounded by enemies, it can't be chilled, slowed or knocked back.</summary>
    [CreateAssetMenu(fileName = "Unstoppable", menuName = "Passives/Class/Unstoppable")]
    public class UnstoppablePassive : PassiveSkill<Unstoppable>
    {
        [Tooltip("Meters around the player that count")]
        [Min(0f)] public float Radius = 4f;
        [Tooltip("Enemies needed within Radius")]
        [Min(1)] public int EnemyCount = 3;

        public override PassiveType Type => PassiveType.Unstoppable;

        protected override Unstoppable CreateComponent() => new Unstoppable { Radius = Radius, EnemyCount = EnemyCount };
    }
}
