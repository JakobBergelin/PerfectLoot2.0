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
    const float ProbeUp = 2f, ProbeDown = 10f, HeightAboveGround = 0.05f;

    /// <summary>
    /// A marker for telegraph at position (flat on the ground), pointing along direction. The quad reaches Size to every
    /// side of position; the shader cuts the shape out of it.
    /// </summary>
    public static void Spawn(EntityCommandBuffer commandBuffer, Entity prefab, in EnemyTelegraph telegraph, Entity caster, int castId, float3 position, float3 direction)
    {
        var marker = commandBuffer.Instantiate(prefab);
        float size = math.max(telegraph.Size, 0.01f);
        float width = telegraph.Shape == TelegraphShape.Line ? telegraph.Width * 0.5f / size : math.radians(telegraph.Width * 0.5f);

        commandBuffer.SetComponent(marker, new LocalTransform { Position = position, Rotation = quaternion.LookRotationSafe(direction, math.up()), Scale = size * 2f });
        commandBuffer.SetComponent(marker, new TelegraphParams { Value = new float4(0f, (float)telegraph.Shape, width, 0f) });
        commandBuffer.SetComponent(marker, new TelegraphMarker { Caster = caster, CastId = castId });
    }

    /// <summary>The spot just above the ground under position, or position itself without ground there.</summary>
    public static float3 OnGround(in PhysicsWorldSingleton physics, float3 position)
    {
        var ray = new RaycastInput
        {
            Start = position + new float3(0f, ProbeUp, 0f),
            End = position - new float3(0f, ProbeDown, 0f),
            Filter = ProjectileUtil.GroundFilter,
        };
        return physics.CastRay(ray, out var hit) ? hit.Position + new float3(0f, HeightAboveGround, 0f) : position;
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
/// Makes the marker prefab at runtime: a flat quad with the TelegraphMarker material from Resources, so no scene has to
/// set it up. Runs again if the singleton is ever gone.
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
            new RenderMeshArray(new[] { material }, new[] { CreateQuad() }), MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
        EntityManager.AddComponentData(prefab, LocalTransform.Identity);
        EntityManager.AddComponentData(prefab, new LocalToWorld { Value = float4x4.identity });
        EntityManager.AddComponentData(prefab, new TelegraphParams());
        EntityManager.AddComponentData(prefab, new TelegraphMarker());
        EntityManager.AddComponent<Prefab>(prefab);

        EntityManager.CreateSingleton(new TelegraphPrefab { Value = prefab });
    }

    // Flat on the ground, 1 meter across; its UVs run -1 to 1 for the shader (+y: forward)
    static Mesh CreateQuad()
    {
        var mesh = new Mesh { name = "TelegraphQuad" };
        mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
        mesh.uv = new[] { new Vector2(-1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
