using UnityEngine;

namespace Equipment
{
    /// <summary>
    /// With Flamethrower equipped, every channel also sprays flames backwards and costs more. Without it,
    /// channeling skills cost less.
    /// </summary>
    [CreateAssetMenu(fileName = "Afterburner", menuName = "Items/Legendary Powers/Afterburner")]
    public class AfterburnerPower : LegendaryPower<Afterburner>
    {
        [Tooltip("Channeling skills cost this much more with Flamethrower equipped (0.3 = 30%)")]
        public float CostIncrease = 0.3f;
        [Tooltip("Channeling skills cost this much less without Flamethrower equipped (0.2 = 20%)")]
        public float CostReduction = 0.2f;

        protected override Afterburner Configure(Afterburner component, bool isNew)
        {
            component.CostIncrease = CostIncrease;
            component.CostReduction = CostReduction;
            return component;
        }
    }
}
