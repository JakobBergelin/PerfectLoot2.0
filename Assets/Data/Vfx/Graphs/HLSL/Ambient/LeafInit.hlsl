#include "Assets/Data/Vfx/Graphs/HLSL/Ambient/AmbientCommon.hlsl"

void LeafInit(inout VFXAttributes attributes, in float3 boundsCenter, in float3 boundsSize, in VFXSampler2D forest,
              in float4x4 forestMatrix, in VFXSampler2D ground, in float4x4 groundMatrix, in float3 colorA, in float3 colorB,
              in float3 colorC)
{
    // Where the forest meets the open (the mask half way)
    float3 best = 0;
    float bestScore = -1;
    for (int i = 0; i < 4; i++)
    {
        float3 spot = AmbientSpot(boundsCenter, boundsSize, VFXRAND2);
        float score = 1.0 - abs(AmbientForest(forest, forestMatrix, spot) - 0.5) * 2.0;
        if (score > bestScore) { best = spot; bestScore = score; }
    }
    attributes.alive = bestScore > 0.4;
    best.y = AmbientGround(ground, groundMatrix, best) + 4.0 + VFXRAND * 2.0;
    attributes.position = best;
    attributes.lifetime = 12.0 + VFXRAND * 4.0;
    attributes.size = 0.45 + VFXRAND * 0.2;
    attributes.angleZ = VFXRAND * 360.0;
    attributes.texIndex = VFXRAND * 100.0;
    float pick = VFXRAND;
    attributes.color = pick < 0.45 ? colorA : (pick < 0.85 ? colorB : colorC);
}
