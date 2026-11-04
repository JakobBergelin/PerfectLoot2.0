using UnityEngine;

namespace Passives
{
    /// <summary>Hits on nearby enemies deal more damage.</summary>
    [CreateAssetMenu(fileName = "Brawler", menuName = "Passives/Brawler")]
    public class BrawlerPassive : PassiveSkill<Brawler>
    {
        [Tooltip("Extra damage to enemies in range (0.2 = +20%)")]
        public float DamageBonus = 0.2f;
        [Tooltip("Meters around the wearer")]
        public float Radius = 4f;

        public override PassiveType Type => PassiveType.Brawler;

        protected override Brawler CreateComponent() => new Brawler
        {
            DamageBonus = DamageBonus,
            Radius = Radius,
        };
    }
}
