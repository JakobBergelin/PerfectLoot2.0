using Unity.Entities;
using UnityEngine;

/// <summary>
/// On the telegraph marker ghost prefab (Prefabs/Net/NetTelegraph): a box (TelegraphBox, 1 meter across and 2 high) with
/// the TelegraphMarker material, whose shader cuts the telegraph's shape out of the ground inside it.
/// </summary>
public class TelegraphAuthoring : MonoBehaviour
{
    class Baker : Baker<TelegraphAuthoring>
    {
        public override void Bake(TelegraphAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new TelegraphParams());
            AddComponent(entity, new TelegraphMarker());
        }
    }
}
