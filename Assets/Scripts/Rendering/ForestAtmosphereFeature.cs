using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The forest's light and air, from each camera's volume (so every split-screen view gets its own): hands CanopyLight
/// and HeightFog to the shaders as global values before the opaques draw, then draws the height fog over the opaques
/// (PL_HeightFog.shader), before transparents. Turn Draw Fog off for a cheaper quality level; the canopy shadows cost
/// next to nothing.
/// </summary>
public class ForestAtmosphereFeature : ScriptableRendererFeature
{
    [SerializeField] Shader fogShader;
    [Tooltip("Draw the height fog (one full-screen pass per camera). Off for a cheaper quality level.")]
    public bool drawFog = true;

    Material _fogMaterial;
    GlobalsPass _globals;
    FogPass _fog;

    public override void Create()
    {
        _globals = new GlobalsPass { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };
        _fog = new FogPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        _fog.ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_globals);
        if (!drawFog) return;
        if (_fogMaterial == null)
        {
            if (fogShader == null) fogShader = Shader.Find("Hidden/PerfectLoot/HeightFog");
            if (fogShader == null) return;
            _fogMaterial = CoreUtils.CreateEngineMaterial(fogShader);
        }
        _fog.Material = _fogMaterial;
        renderer.EnqueuePass(_fog);
    }

    protected override void Dispose(bool disposing) => CoreUtils.Destroy(_fogMaterial);

    static readonly int CanopyParamsId = Shader.PropertyToID("_PL_CanopyParams"), CanopyCloudsId = Shader.PropertyToID("_PL_CanopyClouds"),
                        CanopyShadeId = Shader.PropertyToID("_PL_CanopyShade"),
                        CanopyHeightId = Shader.PropertyToID("_PL_CanopyHeight"), FogColorId = Shader.PropertyToID("_PL_FogColor"),
                        FogParamsId = Shader.PropertyToID("_PL_FogParams"), FogShapeId = Shader.PropertyToID("_PL_FogShape");

    /// <summary>The volume's values as global shader values, for this camera.</summary>
    class GlobalsPass : ScriptableRenderPass
    {
        class Data { public Vector4 CanopyParams, CanopyShade, CanopyClouds, FogColor, FogParams, FogShape; public float CanopyHeight; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var stack = VolumeManager.instance.stack;
            var canopy = stack.GetComponent<CanopyLight>();
            var fog = stack.GetComponent<HeightFog>();
            using var builder = renderGraph.AddUnsafePass<Data>("PL Forest Atmosphere Globals", out var data);
            data.CanopyParams = new Vector4(canopy.strength.value, 1f / canopy.puffSize.value, canopy.coverage.value, canopy.sway.value);
            data.CanopyShade = new Vector4(canopy.hatching.value, canopy.edgeSoftness.value, 0f, 0f);
            var drift = canopy.cloudDrift.value;
            data.CanopyClouds = new Vector4(1f / canopy.cloudSize.value, canopy.cloudCoverage.value, drift.x, drift.y);
            data.CanopyHeight = canopy.canopyHeight.value;
            data.FogColor = fog.color.value;
            data.FogParams = new Vector4(fog.intensity.value, fog.density.value, fog.bands.value, fog.shafts.value);
            data.FogShape = new Vector4(fog.heightOpen.value, fog.heightForest.value, 1f / fog.patchSize.value, fog.forestDensity.value);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(static (Data d, UnsafeGraphContext context) =>
            {
                context.cmd.SetGlobalVector(CanopyParamsId, d.CanopyParams);
                context.cmd.SetGlobalVector(CanopyShadeId, d.CanopyShade);
                context.cmd.SetGlobalVector(CanopyCloudsId, d.CanopyClouds);
                context.cmd.SetGlobalFloat(CanopyHeightId, d.CanopyHeight);
                context.cmd.SetGlobalVector(FogColorId, d.FogColor);
                context.cmd.SetGlobalVector(FogParamsId, d.FogParams);
                context.cmd.SetGlobalVector(FogShapeId, d.FogShape);
            });
        }
    }

    /// <summary>The height fog, blended over the camera's color from its depth.</summary>
    class FogPass : ScriptableRenderPass
    {
        public Material Material;
        static readonly int DriftId = Shader.PropertyToID("_Drift");

        class Data { public Material Material; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var fog = VolumeManager.instance.stack.GetComponent<HeightFog>();
            if (Material == null || !fog.IsActive()) return;
            var camera = frameData.Get<UniversalCameraData>();
            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection) return;
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;

            using var builder = renderGraph.AddRasterRenderPass<Data>("PL Height Fog", out var data);
            Material.SetVector(DriftId, fog.drift.value);
            data.Material = Material;
            builder.SetRenderAttachment(resources.activeColorTexture, 0);
            builder.UseTexture(resources.cameraDepthTexture);
            builder.SetRenderFunc(static (Data d, RasterGraphContext context) =>
                Blitter.BlitTexture(context.cmd, new Vector4(1f, 1f, 0f, 0f), d.Material, 0));
        }
    }
}
