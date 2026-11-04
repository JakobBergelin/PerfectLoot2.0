using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

public static class TelegraphUtil
{
    const float ProbeUp = 2f, ProbeDown = 10f;
    // How far above and below position the marker reaches: a slope of up to 50 degrees all the way out to Size
    const float MinReach = 1.5f, ReachPerSize = 1.2f;

    /// <summary>
    /// A marker for telegraph at position, pointing along direction. The box reaches Size to every side of position; the
    /// shader cuts the shape out of the ground inside it.
    /// </summary>
    public static void Spawn(EntityCommandBuffer commandBuffer, Entity prefab, in EnemyTelegraph telegraph, Entity caster, int castId, float3 position, float3 direction)
    {
        var marker = commandBuffer.Instantiate(prefab);
        float size = math.max(telegraph.Size, 0.01f);
        float width = telegraph.Shape == TelegraphShape.Line ? telegraph.Width * 0.5f / size : math.radians(telegraph.Width * 0.5f);
        float reach = math.max(MinReach, size * ReachPerSize) / size;

        commandBuffer.SetComponent(marker, new LocalTransform { Position = position, Rotation = quaternion.LookRotationSafe(direction, math.up()), Scale = size * 2f });
        commandBuffer.SetComponent(marker, new TelegraphParams { Value = new float4(0f, (float)telegraph.Shape, width, reach) });
        commandBuffer.SetComponent(marker, new TelegraphMarker { Caster = caster, CastId = castId });
    }

    /// <summary>The spot on the ground under position, or position itself without ground there.</summary>
    public static float3 OnGround(in PhysicsWorldSingleton physics, float3 position)
    {
        var ray = new RaycastInput
        {
            Start = position + new float3(0f, ProbeUp, 0f),
            End = position - new float3(0f, ProbeDown, 0f),
            Filter = ProjectileUtil.GroundFilter,
        };
        return physics.CastRay(ray, out var hit) ? hit.Position : position;
    }
}

/// <summary>Fills each marker as its cast goes on and removes it when the cast ends, is cancelled or its caster is gone.</summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
[BurstCompile]
public partial struct TelegraphMarkerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TelegraphMarker>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var casts = SystemAPI.GetComponentLookup<EnemySkillCast>(true);
        state.CompleteDependency();  // The lookup is used here on the main thread

        foreach (var (marker, parameters, entity) in SystemAPI.Query<RefRO<TelegraphMarker>, RefRW<TelegraphParams>>().WithEntityAccess())
        {
            var caster = marker.ValueRO.Caster;
            if (!casts.HasComponent(caster) || !casts[caster].Active || casts[caster].CastId != marker.ValueRO.CastId)
            {
                commandBuffer.DestroyEntity(entity);
                continue;
            }
            var cast = casts[caster];
            parameters.ValueRW.Value.x = 1f - math.saturate(cast.TimeRemaining / cast.Duration);
        }
    }
}

/// <summary>
/// Makes the marker prefab at runtime: a box with the TelegraphMarker material from Resources, so no scene has to set it
/// up. Runs again if the singleton is ever gone.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class TelegraphPrefabSystem : SystemBase
{
    const string MaterialPath = "TelegraphMarker";

    protected override void OnUpdate()
    {
        if (SystemAPI.HasSingleton<TelegraphPrefab>()) return;

        var material = Resources.Load<UnityEngine.Material>(MaterialPath);
        if (material == null)
        {
            Debug.LogError($"TelegraphPrefabSystem: no material Resources/{MaterialPath}.");
            Enabled = false;
            return;
        }

        var prefab = EntityManager.CreateEntity();
        RenderMeshUtility.AddComponents(prefab, EntityManager, new RenderMeshDescription(ShadowCastingMode.Off),
            new RenderMeshArray(new[] { material }, new[] { CreateBox() }), MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
        EntityManager.AddComponentData(prefab, LocalTransform.Identity);
        EntityManager.AddComponentData(prefab, new LocalToWorld { Value = float4x4.identity });
        EntityManager.AddComponentData(prefab, new TelegraphParams());
        EntityManager.AddComponentData(prefab, new TelegraphMarker());
        EntityManager.AddComponent<Prefab>(prefab);

        EntityManager.CreateSingleton(new TelegraphPrefab { Value = prefab });
    }

    // 1 meter across and 2 high, around its center: tall enough for the marker's reach on the steepest slopes
    static Mesh CreateBox()
    {
        var mesh = new Mesh { name = "TelegraphBox" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -1f, -0.5f), new Vector3(-0.5f, 1f, -0.5f), new Vector3(0.5f, 1f, -0.5f), new Vector3(0.5f, -1f, -0.5f),
            new Vector3(-0.5f, -1f, 0.5f), new Vector3(-0.5f, 1f, 0.5f), new Vector3(0.5f, 1f, 0.5f), new Vector3(0.5f, -1f, 0.5f),
        };
        mesh.triangles = new[]
        {
            0, 1, 2, 0, 2, 3,   // Back
            7, 6, 5, 7, 5, 4,   // Front
            4, 5, 1, 4, 1, 0,   // Left
            3, 2, 6, 3, 6, 7,   // Right
            1, 5, 6, 1, 6, 2,   // Top
            3, 7, 4, 3, 4, 0,   // Bottom
        };
        mesh.RecalculateBounds();
        return mesh;
    }
}
