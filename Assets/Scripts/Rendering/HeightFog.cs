using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Low mist over the level's ground (PL_HeightFog.shader), cut into hard comic bands, drifting, deeper and thicker
/// toward the forest (the level's forest mask), with light shafts where the sun falls through gaps in the canopy
/// (CanopyLight). Drawn before transparents, so telegraphs, water, bars and VFX stay on top of it. Set per biome in its
/// volume profile; ForestAtmosphereFeature draws it.
/// </summary>
[Serializable, VolumeComponentMenu("PerfectLoot/Height Fog"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public sealed class HeightFog : VolumeComponent
{
    [Tooltip("How strong the mist is (0 = off).")]
    public ClampedFloatParameter intensity = new(0f, 0f, 1f);
    public ColorParameter color = new(new Color(0.75f, 0.88f, 0.84f), false, false, true);
    [Tooltip("Meters above the ground the mist reaches in the open.")]
    public MinFloatParameter heightOpen = new(0.35f, 0f);
    [Tooltip("Meters above the ground the mist reaches in the forest.")]
    public MinFloatParameter heightForest = new(2.5f, 0f);
    [Tooltip("How thick the mist is (per m of view ray inside it).")]
    public MinFloatParameter density = new(0.3f, 0f);
    [Tooltip("How much thicker it is in the forest than in the open.")]
    public MinFloatParameter forestDensity = new(2f, 1f);
    [Tooltip("Hard steps of mist (0 = smooth).")]
    public ClampedIntParameter bands = new(4, 0, 8);
    [Tooltip("Meters across one mist patch.")]
    public MinFloatParameter patchSize = new(9f, 0.5f);
    [Tooltip("How fast the patches drift (m/s, world x and z).")]
    public Vector2Parameter drift = new(new Vector2(0.4f, 0.15f));
    [Tooltip("How much brighter the mist is where sun falls through it (light shafts; needs Canopy Light).")]
    public ClampedFloatParameter shafts = new(0.6f, 0f, 2f);

    public bool IsActive() => intensity.value > 0f;
}
