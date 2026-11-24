using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The forest's light and air, from the camera's volume: hands CanopyLight and HeightFog to the shaders as global
/// values before the opaques draw, then draws the height fog over the opaques (PL_HeightFog.shader), before
/// transparents, and the TiltShift blur before post-processing (PL_TiltShift.shader). Turn Draw Fog or Draw Tilt Shift off
/// for a cheaper quality level; the canopy shadows cost next to nothing.
/// </summary>
public class ForestAtmosphereFeature : ScriptableRendererFeature
{
    [SerializeField] Shader fogShader;
    [SerializeField] Shader tiltShiftShader;
    [Tooltip("Draw the height fog (one full-screen pass per camera). Off for a cheaper quality level.")]
    public bool drawFog = true;
    [Tooltip("Draw the tilt-shift blur (one full-screen pass per camera). Off for a cheaper quality level.")]
    public bool drawTiltShift = true;

    Material _fogMaterial, _tiltShiftMaterial;
    GlobalsPass _globals;
    FogPass _fog;
    TiltShiftPass _tiltShift;

    public override void Create()
    {
        _globals = new GlobalsPass { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };
        // Normals before the opaques make URP draw the depth texture in a prepass, which has no water (PL_Liquid has no
        // DepthNormals pass): the water reads it for its foam ring
        _globals.ConfigureInput(ScriptableRenderPassInput.Normal);
        _fog = new FogPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        _fog.ConfigureInput(ScriptableRenderPassInput.Depth);
        // Swaps the camera color for its blurred copy, so URP must keep the color in a texture until post-processing
        _tiltShift = new TiltShiftPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing, requiresIntermediateTexture = true };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_globals);
        if (drawFog && Load(ref fogShader, "Hidden/PerfectLoot/HeightFog", ref _fogMaterial))
        {
            _fog.Material = _fogMaterial;
            renderer.EnqueuePass(_fog);
        }
        if (drawTiltShift && Load(ref tiltShiftShader, "Hidden/PerfectLoot/TiltShift", ref _tiltShiftMaterial))
        {
            _tiltShift.Material = _tiltShiftMaterial;
            renderer.EnqueuePass(_tiltShift);
        }
    }

    static bool Load(ref Shader shader, string name, ref Material material)
    {
        if (material != null) return true;
        if (shader == null) shader = Shader.Find(name);
        if (shader == null) return false;
        material = CoreUtils.CreateEngineMaterial(shader);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_fogMaterial);
        CoreUtils.Destroy(_tiltShiftMaterial);
    }

    static readonly int CanopyParamsId = Shader.PropertyToID("_PL_CanopyParams"), CanopyCloudsId = Shader.PropertyToID("_PL_CanopyClouds"),
                        CanopyShadeId = Shader.PropertyToID("_PL_CanopyShade"),
                        CanopyHeightId = Shader.PropertyToID("_PL_CanopyHeight"), WaterDepthFoamId = Shader.PropertyToID("_PL_WaterDepthFoam"), FogColorId = Shader.PropertyToID("_PL_FogColor"),
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
                context.cmd.SetGlobalFloat(WaterDepthFoamId, 1f);
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

    /// <summary>The tilt-shift blur: the camera color blurred into a copy, which becomes the camera color.</summary>
    class TiltShiftPass : ScriptableRenderPass
    {
        public Material Material;
        static readonly int TiltShiftId = Shader.PropertyToID("_TiltShift");

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var tilt = VolumeManager.instance.stack.GetComponent<TiltShift>();
            if (Material == null || !tilt.IsActive()) return;
            var camera = frameData.Get<UniversalCameraData>();
            if (camera.cameraType != CameraType.Game) return;
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;

            Material.SetVector(TiltShiftId, new Vector4(tilt.strength.value, tilt.focusHeight.value, tilt.softness.value, 0f));
            var source = resources.cameraColor;
            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "PL Tilt Shift";
            desc.clearBuffer = false;
            var blurred = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, blurred, Material, 0), "PL Tilt Shift");
            resources.cameraColor = blurred;
        }
    }
}
