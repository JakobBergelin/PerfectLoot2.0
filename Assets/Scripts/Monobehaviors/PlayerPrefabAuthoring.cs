using UnityEngine;
using Unity.Entities;

/// <summary>
/// Put in a subscene: bakes the player's visual prefab into an entity prefab that CharacterManager spawns.
/// (A MonoBehaviour outside a subscene can't convert a GameObject prefab to an entity prefab itself.)
/// </summary>
public class PlayerPrefabAuthoring : MonoBehaviour
{
    public GameObject PlayerVisualPrefab;

    public class Baker : Baker<PlayerPrefabAuthoring>
    {
        public override void Bake(PlayerPrefabAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new PlayerPrefab
            {
                Value = GetEntity(authoring.PlayerVisualPrefab, TransformUsageFlags.Dynamic)
            });
        }
    }
}

/// <summary>Singleton holding the baked player visual prefab.</summary>
public struct PlayerPrefab : IComponentData
{
    public Entity Value;
}
