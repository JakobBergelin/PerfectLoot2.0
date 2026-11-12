using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Ghost prefabs the host makes at runtime that no other prefab holds: the NetVfx stand-in and the telegraph marker
/// (Prefabs/Net). In CoreEntities.
/// </summary>
public class NetPrefabsAuthoring : MonoBehaviour
{
    [FormerlySerializedAs("Projectile")] public GameObject Vfx;
    public GameObject Telegraph;

    class Baker : Baker<NetPrefabsAuthoring>
    {
        public override void Bake(NetPrefabsAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new NetPrefabs { Vfx = PrefabEntity(authoring.Vfx), Telegraph = PrefabEntity(authoring.Telegraph) });
        }

        Entity PrefabEntity(GameObject prefab) => prefab != null ? GetEntity(prefab, TransformUsageFlags.Dynamic) : Entity.Null;
    }
}

/// <summary>Singleton with the runtime ghost prefabs (NetPrefabsAuthoring).</summary>
public struct NetPrefabs : IComponentData
{
    public Entity Vfx;
    public Entity Telegraph;
}
