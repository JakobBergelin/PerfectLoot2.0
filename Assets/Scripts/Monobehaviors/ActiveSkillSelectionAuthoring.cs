using UnityEngine;
using Unity.Entities;

/// <summary>Gives the entity an empty Skill buffer. ActiveSkillManager fills it with the equipped skills.</summary>
public class ActiveSkillSelectionAuthoring : MonoBehaviour
{
    public class Baker : Baker<ActiveSkillSelectionAuthoring>
    {
        public override void Bake(ActiveSkillSelectionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddBuffer<Skill>(entity);
        }
    }
}
