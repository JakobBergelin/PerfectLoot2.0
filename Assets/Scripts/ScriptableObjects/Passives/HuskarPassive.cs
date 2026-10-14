using UnityEngine;

namespace Passives
{
    /// <summary>Skills cost health instead of resource.</summary>
    [CreateAssetMenu(fileName = "Huskar", menuName = "Passives/Huskar")]
    public class HuskarPassive : PassiveSkill<Huskar>
    {
        public override PassiveType Type => PassiveType.Huskar;

        protected override Huskar CreateComponent() => new Huskar();
    }
}
