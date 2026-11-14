using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>On a player whose model HiddenModelSystem is hiding.</summary>
public struct ModelHidden : IComponentData {}

/// <summary>A render part HiddenModelSystem hid (it gets DisableRendering back off when the model shows again).</summary>
public struct HiddenModelPart : IComponentData {}

/// <summary>
/// Hides a player's model, on every machine, while its NetPlayerActions say it's Phasing (Phantom Edge), Rolling (Sonic
/// Ball) or Flying (Ball Lightning): those skills' effects take its place. The model is everything under its top bone
/// (body parts and the equipment on the bones), not its bars. Parts that show up meanwhile (new equipment) are hidden too,
/// and only parts it hid come back. Mirror Image copies have NetPlayerActions too. Structural changes, so on the main thread.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
public partial class HiddenModelSystem : SystemBase
{
    const ushort Hiding = NetPlayerActions.Phasing | NetPlayerActions.Rolling | NetPlayerActions.Flying;

    readonly List<Entity> _parts = new List<Entity>();

    protected override void OnCreate() => RequireForUpdate<NetPlayerActions>();

    protected override void OnUpdate()
    {
        var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        foreach (var (actions, bones, entity) in SystemAPI.Query<RefRO<NetPlayerActions>, DynamicBuffer<RigidBone>>().WithEntityAccess())
        {
            bool hide = actions.ValueRO.Has(Hiding);
            bool hidden = EntityManager.HasComponent<ModelHidden>(entity);
            if ((!hide && !hidden) || bones.Length == 0) continue;

            var top = bones[0].Value;   // RigidModelBuilder lists parents first: the model's top bone
            _parts.Clear();
            if (EntityManager.Exists(top)) Collect(top);
            foreach (var part in _parts)
            {
                if (!EntityManager.HasComponent<MaterialMeshInfo>(part)) continue;
                bool hiddenByUs = EntityManager.HasComponent<HiddenModelPart>(part);
                if (hide && !hiddenByUs && !EntityManager.HasComponent<DisableRendering>(part))
                {
                    commandBuffer.AddComponent<DisableRendering>(part);
                    commandBuffer.AddComponent<HiddenModelPart>(part);
                }
                else if (!hide && hiddenByUs)
                {
                    commandBuffer.RemoveComponent<DisableRendering>(part);
                    commandBuffer.RemoveComponent<HiddenModelPart>(part);
                }
            }
            if (hide && !hidden) commandBuffer.AddComponent<ModelHidden>(entity);
            else if (!hide && hidden) commandBuffer.RemoveComponent<ModelHidden>(entity);
        }
        commandBuffer.Playback(EntityManager);
        commandBuffer.Dispose();
    }

    // Every entity under it, including parts attached after it was made (equipment), which its LinkedEntityGroup misses
    void Collect(Entity entity)
    {
        _parts.Add(entity);
        if (!EntityManager.HasBuffer<Child>(entity)) return;
        using var children = EntityManager.GetBuffer<Child>(entity).ToNativeArray(Allocator.Temp);
        foreach (var child in children) Collect(child.Value);
    }
}
