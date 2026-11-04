using UnityEngine;

namespace Passives
{
    /// <summary>Channeling lowers damage taken and adds health regeneration.</summary>
    [CreateAssetMenu(fileName = "Meditation", menuName = "Passives/Meditation")]
    public class MeditationPassive : PassiveSkill<Meditation>
    {
        [Tooltip("Less damage taken while channeling (0.2 = -20%)")]
        public float DamageReduction = 0.2f;
        [Tooltip("Health per second added while channeling")]
        public float HealthRegeneration = 2f;

        public override PassiveType Type => PassiveType.Meditation;

        protected override Meditation CreateComponent() => new Meditation
        {
            DamageReduction = DamageReduction,
            HealthRegeneration = HealthRegeneration,
        };
    }
}
