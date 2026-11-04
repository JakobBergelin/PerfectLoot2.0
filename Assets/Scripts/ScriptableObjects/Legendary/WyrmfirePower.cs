using UnityEngine;

namespace Equipment
{
    /// <summary>Enemies that hit the wearer in melee start burning.</summary>
    [CreateAssetMenu(fileName = "Wyrmfire", menuName = "Items/Legendary Powers/Wyrmfire")]
    public class WyrmfirePower : LegendaryPower<Wyrmfire>
    {
        [Tooltip("Fire damage each burn deals in total, before the wearer's damage %")]
        public float Damage = 4f;
        [Tooltip("Seconds each burn lasts")]
        public float Duration = 3f;

        protected override Wyrmfire Configure(Wyrmfire component, bool isNew)
        {
            component.Damage = Damage;
            component.Duration = Duration;
            return component;
        }
    }
}
