using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

/// <summary>
/// What the player wears, by visual key (the asset name of the item's visual base, see RollableItem.VisualKey).
/// Written by InventoryManager; EquipmentVisualSystem turns it into part entities on the socket bones.
/// </summary>
public struct EquippedVisual : IBufferElementData
{
    public FixedString64Bytes Key;
}

/// <summary>Added with a new EquippedVisual list; EquipmentVisualSystem rebuilds the parts and removes it.</summary>
public struct EquipmentVisualsChanged : IComponentData { }

/// <summary>A bone parts can be attached to, by name (head, chest, hand_R, ...).</summary>
public struct EquipmentSocket : IBufferElementData
{
    public FixedString32Bytes Name;
    public Entity Bone;
}

/// <summary>The part entities currently attached, so they can be removed when the equipment changes.</summary>
public struct EquipmentPartInstance : IBufferElementData
{
    public Entity Value;
}

/// <summary>Registry singleton (EquipmentVisualsAuthoring): one baked part prefab per item visual and socket.</summary>
public struct EquipmentPartPrefab : IBufferElementData
{
    public FixedString64Bytes Key;
    public FixedString32Bytes Socket;
    public Entity Prefab;
}

/// <summary>
/// Rebuilds a unit's equipment parts when its EquippedVisual list changes: every registered part of every equipped
/// visual is instantiated and parented to its socket bone (the parts are modelled around that bone's rest position, so
/// they sit at its origin and follow its animation). Before transforms are calculated, like the other visual feedback.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
public partial class EquipmentVisualSystem : SystemBase
{
    EntityQuery _changed;

    protected override void OnCreate()
    {
        _changed = SystemAPI.QueryBuilder().WithAll<EquipmentVisualsChanged, EquippedVisual, EquipmentSocket, EquipmentPartInstance>().Build();
        RequireForUpdate(_changed);
        RequireForUpdate<EquipmentPartPrefab>();
    }

    protected override void OnUpdate()
    {
        var registry = SystemAPI.GetSingletonBuffer<EquipmentPartPrefab>(true).ToNativeArray(Allocator.Temp);
        using var units = _changed.ToEntityArray(Allocator.Temp);
        foreach (var unit in units)
        {
            var old = EntityManager.GetBuffer<EquipmentPartInstance>(unit).ToNativeArray(Allocator.Temp);
            foreach (var part in old)
                if (EntityManager.Exists(part.Value)) EntityManager.DestroyEntity(part.Value);
            EntityManager.GetBuffer<EquipmentPartInstance>(unit).Clear();

            var keys = EntityManager.GetBuffer<EquippedVisual>(unit).ToNativeArray(Allocator.Temp);
            var sockets = EntityManager.GetBuffer<EquipmentSocket>(unit).ToNativeArray(Allocator.Temp);
            var created = new NativeList<Entity>(Allocator.Temp);
            foreach (var key in keys)
            {
                foreach (var entry in registry)
                {
                    if (entry.Key != key.Key) continue;
                    var bone = Entity.Null;
                    foreach (var socket in sockets)
                        if (socket.Name == entry.Socket) bone = socket.Bone;
                    if (bone == Entity.Null || !EntityManager.Exists(bone)) continue;

                    var part = EntityManager.Instantiate(entry.Prefab);
                    EntityManager.AddComponentData(part, new Parent { Value = bone });
                    EntityManager.SetComponentData(part, LocalTransform.Identity);
                    created.Add(part);
                }
            }
            var instances = EntityManager.GetBuffer<EquipmentPartInstance>(unit);
            foreach (var part in created) instances.Add(new EquipmentPartInstance { Value = part });
            EntityManager.RemoveComponent<EquipmentVisualsChanged>(unit);
        }
    }
}
