using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

/// <summary>Briefly tints a unit toward a flash color when DamagingSystem starts its HitFlash.</summary>
public struct HitFlash : IComponentData
{
    public float4 BaseColor;    // The material's own color (linear), restored when the flash ends
    public float4 FlashColor;
    public float Timer;         // Seconds left of the flash
    public bool Flashing;       // Still needs the base color written back
}

public static class HitFlashBaking
{
    /// <summary>Adds HitFlash and a base color override, starting from the renderer's material color.</summary>
    public static void Add(IBaker baker, Entity entity)
    {
        var renderer = baker.GetComponent<MeshRenderer>();
        var material = renderer != null ? renderer.sharedMaterial : null;
        baker.DependsOn(material);

        var color = material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor").linear : Color.white;
        var baseColor = new float4(color.r, color.g, color.b, color.a);
        baker.AddComponent(entity, new URPMaterialPropertyBaseColor { Value = baseColor });
        baker.AddComponent(entity, new HitFlash { BaseColor = baseColor });
    }
}

[UpdateAfter(typeof(DamagingSystem))]
[BurstCompile]
public partial struct HitFlashSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<HitFlashConfig>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<HitFlashConfig>();
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (flash, color) in SystemAPI.Query<RefRW<HitFlash>, RefRW<URPMaterialPropertyBaseColor>>())
        {
            if (flash.ValueRO.Timer > 0f)
            {
                // Strongest at the hit, fading back to the base color
                float t = config.Duration > 0f ? config.Strength * flash.ValueRO.Timer / config.Duration : 0f;
                color.ValueRW.Value = math.lerp(flash.ValueRO.BaseColor, flash.ValueRO.FlashColor, math.saturate(t));
                flash.ValueRW.Timer -= deltaTime;
                flash.ValueRW.Flashing = true;
            }
            else if (flash.ValueRO.Flashing)
            {
                color.ValueRW.Value = flash.ValueRO.BaseColor;
                flash.ValueRW.Flashing = false;
            }
        }
    }
}
