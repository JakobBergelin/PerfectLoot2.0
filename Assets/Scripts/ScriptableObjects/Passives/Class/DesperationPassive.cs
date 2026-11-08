using UnityEngine;

namespace Passives
{
    /// <summary>Caster's class passive: cooldowns tick faster the lower your health is.</summary>
    [CreateAssetMenu(fileName = "Desperation", menuName = "Passives/Class/Desperation")]
    public class DesperationPassive : PassiveSkill<Desperation>
    {
        [Tooltip("Health (share of max) below which cooldowns start to speed up")]
        [Range(0f, 1f)] public float StartHealth = 0.5f;
        [Tooltip("Health (share of max) at and below which they tick at the full speed-up")]
        [Range(0f, 1f)] public float FullHealth = 0.1f;
        [Tooltip("How much faster cooldowns tick at most, 0.25 = 25% faster")]
        [Min(0f)] public float MaxSpeedUp = 0.25f;

        public override PassiveType Type => PassiveType.Desperation;

        protected override Desperation CreateComponent() => new Desperation { StartHealth = StartHealth, FullHealth = FullHealth, MaxSpeedUp = MaxSpeedUp };
    }
}
