using UnityEngine;

namespace Passives
{
    /// <summary>While Invulnerable, the player is Phased: it moves through enemies and they through it.</summary>
    [CreateAssetMenu(fileName = "Ghost", menuName = "Passives/Ghost")]
    public class GhostPassive : PassiveSkill<Ghost>
    {
        public override PassiveType Type => PassiveType.Ghost;

        protected override Ghost CreateComponent() => new Ghost();
    }
}
