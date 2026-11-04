using UnityEngine;

namespace Equipment
{
    /// <summary>Skills with a cooldown deal more damage the longer their cooldown is.</summary>
    [CreateAssetMenu(fileName = "Patience", menuName = "Items/Legendary Powers/Patience")]
    public class PatiencePower : LegendaryPower<Patience>
    {
        [Tooltip("Extra damage per second of a skill's base cooldown (0.15 = +15%). Cooldown reduction doesn't lower it")]
        public float DamagePerCooldownSecond = 0.15f;

        protected override Patience Configure(Patience component, bool isNew)
        {
            component.DamagePerCooldownSecond = DamagePerCooldownSecond;
            return component;
        }
    }
}
