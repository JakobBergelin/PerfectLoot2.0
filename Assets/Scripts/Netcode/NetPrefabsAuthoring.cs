using Unity.Entities;
using UnityEngine;

/// <summary>
/// SPIKE: ghost prefabs for things the simulation makes at runtime without a prefab of their own (projectiles), so the
/// server can instantiate a ghost instead of an empty entity and clients see it. In CoreEntities.
/// </summary>
public class NetPrefabsAuthoring : MonoBehaviour
{
    public GameObject Projectile;

    class Baker : Baker<NetPrefabsAuthoring>
    {
        public override void Bake(NetPrefabsAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new NetPrefabs { Projectile = authoring.Projectile != null ? GetEntity(authoring.Projectile, TransformUsageFlags.Dynamic) : Entity.Null });
        }
    }
}

/// <summary>SPIKE: singleton with the runtime ghost prefabs (NetPrefabsAuthoring). Absent outside a networked game's subscenes.</summary>
public struct NetPrefabs : IComponentData
{
    public Entity Projectile;
}
