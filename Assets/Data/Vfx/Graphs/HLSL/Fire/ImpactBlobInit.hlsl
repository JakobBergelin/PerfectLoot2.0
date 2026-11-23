#include "Assets/Data/Vfx/Graphs/HLSL/Fire/FireCommon.hlsl"

void ImpactBlobInit(inout VFXAttributes attributes, in float3 origin)
{
    float3 dir = normalize(VFXRAND3 - 0.5 + float3(0.0, 0.25, 0.0));
    attributes.position = origin + dir * 0.2;
    attributes.velocity = dir * (2.0 + VFXRAND * 2.5);
    attributes.lifetime = 0.35 + VFXRAND * 0.25;
    attributes.size = 0.8 + VFXRAND * 0.5;
    attributes.angleZ = VFXRAND * 360.0;
}
