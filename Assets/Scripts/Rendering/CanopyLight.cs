using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Sunlight through a canopy (PL_Canopy.hlsl): leaf shade shaped like cartoon clouds, hanging at the canopy height and
/// projected down along the sun, and big puffy cloud shadows drifting over. In PerfectLoot/Comic the leaf shade is its
/// own lighter tone between sun and shadow, with light hatching. Set per biome in its volume profile;
/// ForestAtmosphereFeature hands it to the shaders.
/// </summary>
[Serializable, VolumeComponentMenu("PerfectLoot/Canopy Light"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public sealed class CanopyLight : VolumeComponent
{
    [Tooltip("How dark the leaf shade is (0 = off, 1 = as dark as full shadow).")]
    public ClampedFloatParameter strength = new(0f, 0f, 1f);
    [Tooltip("How much hatching the leaf shade gets (0 = none).")]
    public ClampedFloatParameter hatching = new(0.3f, 0f, 1f);
    [Tooltip("How soft the shade's edges are (0 = hard comic edge).")]
    public ClampedFloatParameter edgeSoftness = new(0.2f, 0f, 1f);
    [Tooltip("Meters across one puff of the leaf shade.")]
    public MinFloatParameter puffSize = new(1.6f, 0.1f);
    [Tooltip("How much of the ground is in leaf shade.")]
    public ClampedFloatParameter coverage = new(0.4f, 0f, 1f);
    [Tooltip("Meters above the ground where the leaves hang. Points near it (treetops) get only the clouds.")]
    public MinFloatParameter canopyHeight = new(6f, 0.5f);
    [Tooltip("Meters the leaves rock in the wind.")]
    public ClampedFloatParameter sway = new(0.25f, 0f, 2f);
    [Tooltip("Meters across one puff of the cloud shadows.")]
    public MinFloatParameter cloudSize = new(12f, 1f);
    [Tooltip("How much of the sky is cloud (0 = none).")]
    public ClampedFloatParameter cloudCoverage = new(0.35f, 0f, 1f);
    [Tooltip("How fast the clouds drift (m/s, world x and z).")]
    public Vector2Parameter cloudDrift = new(new Vector2(1.2f, 0.5f));

    public bool IsActive() => strength.value > 0f;
}
