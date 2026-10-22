using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

/// <summary>
/// Destroys every entity tagged DestroyWithChildren together with its whole hierarchy (Child buffers, recursively)
/// and its LinkedEntityGroup, all in the same frame. Runs last, after EndSimulationEntityCommandBufferSystem, so tags
/// added during the frame or through that command buffer are handled this frame.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]
[BurstCompile]
public partial struct DestroyWithChildrenSystem : ISystem
{
    EntityQuery _query;

    public void OnCreate(ref SystemState state)
    {
        _query = SystemAPI.QueryBuilder().WithAll<DestroyWithChildren>().Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (_query.IsEmpty) return;

        var entityManager = state.EntityManager;
        var children = SystemAPI.GetBufferLookup<Child>(true);
        var linked = SystemAPI.GetBufferLookup<LinkedEntityGroup>(true);

        // Gather the whole tree of every tagged entity, each entity once
        var roots = _query.ToEntityArray(Allocator.Temp);
        var toDestroy = new NativeHashSet<Entity>(roots.Length * 4, Allocator.Temp);
        var stack = new NativeList<Entity>(roots.Length, Allocator.Temp);
        stack.AddRange(roots);

        while (stack.Length > 0)
        {
            var entity = stack[stack.Length - 1];
            stack.RemoveAt(stack.Length - 1);
            if (!entityManager.Exists(entity) || !toDestroy.Add(entity)) continue;

            if (children.HasBuffer(entity))
                foreach (var child in children[entity]) stack.Add(child.Value);
            if (linked.HasBuffer(entity))
                foreach (var member in linked[entity]) stack.Add(member.Value);
        }

        // Every linked member is already in the list, so drop the groups first: DestroyEntity would otherwise also
        // destroy them through the group and then hit them again as already destroyed
        var entities = toDestroy.ToNativeArray(Allocator.Temp);
        entityManager.RemoveComponent<LinkedEntityGroup>(entities);
        entityManager.DestroyEntity(entities);
    }
}
