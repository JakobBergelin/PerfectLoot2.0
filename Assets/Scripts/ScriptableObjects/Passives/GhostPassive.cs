using Unity.Entities;
using UnityEngine;

namespace Passives
{
    /// <summary>While Invulnerable, the player is Phased: it moves through enemies and they through it.</summary>
    [CreateAssetMenu(fileName = "Ghost", menuName = "Passives/Ghost")]
    public class GhostPassive : PassiveSkill<Ghost>
    {
        public override PassiveType Type => PassiveType.Ghost;

        protected override Ghost CreateComponent() => new Ghost();

        // GhostSystem stops with the component, so Phased would stick if it comes off while Invulnerable
        public override void Remove(EntityManager entityManager, Entity entity)
        {
            base.Remove(entityManager, entity);
            if (entityManager.HasComponent<Phased>(entity)) entityManager.SetComponentEnabled<Phased>(entity, false);
        }
    }
}
