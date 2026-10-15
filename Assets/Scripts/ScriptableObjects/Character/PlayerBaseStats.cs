using UnityEngine;

namespace Characters
{
    /// <summary>The player's base stats before class, gear, passives and buffs. Used by CharacterManager when spawning.</summary>
    [CreateAssetMenu(fileName = "PlayerBaseStats", menuName = "Character/Player Base Stats")]
    public class PlayerBaseStats : ScriptableObject
    {
        public StatBlock Stats = new StatBlock
        {
            Health = 10f,
            HealthRegeneration = 1f,
            AttackSpeed = 1f,
            CriticalChance = 0.05f,
            CriticalDamage = 1f,
        };
    }
}
