#include "Assets/Data/Vfx/Graphs/HLSL/Fire/FireCommon.hlsl"

void BlobInit(inout VFXAttributes attributes, in float3 origin)
{
    attributes.position = origin + (VFXRAND3 - 0.5) * 0.4;
    attributes.velocity = (VFXRAND3 - 0.5) * 0.6 + float3(0.0, 0.5, 0.0);
    attributes.lifetime = 0.25 + VFXRAND * 0.2;
    attributes.size = 0.3 + VFXRAND * 0.15;
    attributes.angleZ = VFXRAND * 360.0;
}
