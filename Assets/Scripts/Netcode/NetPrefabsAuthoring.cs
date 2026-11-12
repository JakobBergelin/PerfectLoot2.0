using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>Ghost prefabs the host makes at runtime that have no prefab of their own: the NetVfx stand-in. In CoreEntities.</summary>
public class NetPrefabsAuthoring : MonoBehaviour
{
    [FormerlySerializedAs("Projectile")] public GameObject Vfx;

    class Baker : Baker<NetPrefabsAuthoring>
    {
        public override void Bake(NetPrefabsAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new NetPrefabs { Vfx = authoring.Vfx != null ? GetEntity(authoring.Vfx, TransformUsageFlags.Dynamic) : Entity.Null });
        }
    }
}

/// <summary>Singleton with the runtime ghost prefabs (NetPrefabsAuthoring).</summary>
public struct NetPrefabs : IComponentData
{
    public Entity Vfx;
}
