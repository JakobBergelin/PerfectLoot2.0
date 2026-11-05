using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Entities;

/// <summary>Put in a subscene: bakes the visual prefabs of companions with a body, for their skills to copy. Looked up by prefab name.</summary>
public class CompanionPrefabsAuthoring : MonoBehaviour
{
    [Tooltip("Visual-only prefabs on the Player layer: mesh, collider, rigidbody and UnitBarsAuthoring")]
    public List<GameObject> Bodies = new List<GameObject>();

    public class Baker : Baker<CompanionPrefabsAuthoring>
    {
        public override void Bake(CompanionPrefabsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            var buffer = AddBuffer<CompanionPrefabElement>(entity);
            foreach (var body in authoring.Bodies)
            {
                if (body == null) continue;
                buffer.Add(new CompanionPrefabElement
                {
                    Key = body.name,
                    Prefab = GetEntity(body, TransformUsageFlags.Dynamic),
                });
            }
        }
    }
}

/// <summary>A baked companion body, keyed by its prefab's name.</summary>
public struct CompanionPrefabElement : IBufferElementData
{
    public FixedString64Bytes Key;
    public Entity Prefab;
}
