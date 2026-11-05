using UnityEngine;
using Unity.Entities;

/// <summary>Put in a subscene: bakes the visual prefabs of companions with a body, for their skills to copy.</summary>
public class CompanionPrefabsAuthoring : MonoBehaviour
{
    [Tooltip("Visual-only prefab on the Player layer: mesh, collider, rigidbody and UnitBarsAuthoring")]
    public GameObject Guardian;

    public class Baker : Baker<CompanionPrefabsAuthoring>
    {
        public override void Bake(CompanionPrefabsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new CompanionPrefabs
            {
                Guardian = GetEntity(authoring.Guardian, TransformUsageFlags.Dynamic)
            });
        }
    }
}

/// <summary>Singleton holding the baked companion prefabs.</summary>
public struct CompanionPrefabs : IComponentData
{
    public Entity Guardian;
}
