#include "Assets/Data/Vfx/Graphs/HLSL/Ambient/AmbientCommon.hlsl"

void PollenInit(inout VFXAttributes attributes, in float3 boundsCenter, in float3 boundsSize, in VFXSampler2D forest,
                in float4x4 forestMatrix, in VFXSampler2D ground, in float4x4 groundMatrix, in float3 color)
{
    float3 best = 0;
    float bestForest = 2;
    for (int i = 0; i < 3; i++)
    {
        float3 spot = AmbientSpot(boundsCenter, boundsSize, VFXRAND2);
        float f = AmbientForest(forest, forestMatrix, spot);
        if (f < bestForest) { best = spot; bestForest = f; }
    }
    attributes.alive = bestForest < 0.6;
    best.y = AmbientGround(ground, groundMatrix, best) + 0.3 + VFXRAND * 2.7;
    attributes.position = best;
    attributes.lifetime = 8.0 + VFXRAND * 4.0;
    attributes.size = 0.05 + VFXRAND * 0.04;
    attributes.color = color;
    attributes.texIndex = VFXRAND * 100.0;
}
