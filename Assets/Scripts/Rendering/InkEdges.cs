using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Comic ink lines inside shapes and where things stand on or in front of the ground (PL_InkEdges.shader), found from
/// the depth and normals. The materials' outlines trace silhouettes only. Drawn after the opaques and before the height
/// fog. Set per biome in its volume profile; ForestAtmosphereFeature draws it.
/// </summary>
[Serializable, VolumeComponentMenu("PerfectLoot/Ink Edges"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public sealed class InkEdges : VolumeComponent
{
    [Tooltip("How dark the lines are (0 = off).")]
    public ClampedFloatParameter strength = new(0f, 0f, 1f);
    [Tooltip("Line width in pixels at 1080p.")]
    public ClampedFloatParameter width = new(1f, 0.5f, 3f);
    [Tooltip("How sharp a step in depth must be to get a line, as a share of its distance from the camera. Lower draws more lines.")]
    public ClampedFloatParameter depthThreshold = new(0.02f, 0.002f, 0.2f);
    [Tooltip("Steps in depth smaller than this many meters get no line, so grass tufts and pebbles keep only their own outline and the floor stays quiet.")]
    public ClampedFloatParameter smallestStep = new(0.4f, 0f, 2f);
    [Tooltip("How sharp a crease must be to get a line, in degrees. Lower draws more lines, also across faceted props.")]
    public ClampedFloatParameter creaseAngle = new(45f, 10f, 90f);
    [Tooltip("Line color (the art guide's ink).")]
    public ColorParameter color = new(new Color(0.0706f, 0.0471f, 0.0863f), false, false, true);

    public bool IsActive() => strength.value > 0f;
}
