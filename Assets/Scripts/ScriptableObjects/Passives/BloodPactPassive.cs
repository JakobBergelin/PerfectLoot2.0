namespace Passives
{
    /// <summary>The wearer's companions' hits use its Life on Hit and Resource on Hit.</summary>
    [UnityEngine.CreateAssetMenu(fileName = "BloodPact", menuName = "Passives/Blood Pact")]
    public class BloodPactPassive : PassiveSkill<BloodPact>
    {
        public override PassiveType Type => PassiveType.BloodPact;

        protected override BloodPact CreateComponent() => new BloodPact();
    }
}
