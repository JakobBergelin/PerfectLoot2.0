using System;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// On a unit's visual prefab root (RigidModelBuilder adds it): the bones equipment parts attach to, named after the bone.
/// Also gives the unit the buffers EquipmentVisualSystem works with.
/// </summary>
public class EquipmentSocketsAuthoring : MonoBehaviour
{
    public Transform[] Sockets = Array.Empty<Transform>();

    public class Baker : Baker<EquipmentSocketsAuthoring>
    {
        public override void Bake(EquipmentSocketsAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            var sockets = AddBuffer<EquipmentSocket>(entity);
            foreach (var socket in authoring.Sockets)
            {
                if (socket == null) continue;
                sockets.Add(new EquipmentSocket { Name = socket.name, Bone = GetEntity(socket, TransformUsageFlags.Dynamic) });
            }
            AddBuffer<EquippedVisual>(entity);
            AddBuffer<EquipmentPartInstance>(entity);
        }
    }
}
