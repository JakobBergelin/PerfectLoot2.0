#include "Assets/Data/Vfx/Graphs/HLSL/Fire/FireCommon.hlsl"

void EmberInit(inout VFXAttributes attributes, in float3 origin)
{
    attributes.position = origin + (VFXRAND3 - 0.5) * 0.3;
    attributes.velocity = (VFXRAND3 - 0.5) * 1.2 + float3(0.0, 0.8 + VFXRAND * 0.8, 0.0);
    attributes.lifetime = 0.4 + VFXRAND * 0.4;
    attributes.size = 0.06 + VFXRAND * 0.05;
}
