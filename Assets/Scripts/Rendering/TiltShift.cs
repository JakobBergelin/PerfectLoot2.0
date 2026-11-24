using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// A miniature-diorama blur at the top and bottom of the screen (PL_TiltShift.shader). Screen space, so it stays the same
/// however far the camera zooms out; the camera keeps the players in the sharp middle. Drawn before post-processing, so
/// bloom and grading go over it and the HUD stays sharp. Set per biome in its volume profile; ForestAtmosphereFeature draws it.
/// </summary>
[Serializable, VolumeComponentMenu("PerfectLoot/Tilt Shift"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public sealed class TiltShift : VolumeComponent
{
    [Tooltip("Blur at the very top and bottom edge, in pixels at 1080p (0 = off).")]
    public ClampedFloatParameter strength = new(0f, 0f, 12f);
    [Tooltip("How much of the screen's height in the middle stays fully sharp.")]
    public ClampedFloatParameter focusHeight = new(0.6f, 0f, 1f);
    [Tooltip("How far past the sharp band the blur takes to fade in, as a share of the way from the middle to the edge.")]
    public ClampedFloatParameter softness = new(0.3f, 0.01f, 1f);

    public bool IsActive() => strength.value > 0f;
}
