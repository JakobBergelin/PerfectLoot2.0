#include "Assets/Data/Vfx/Graphs/HLSL/Fire/FireCommon.hlsl"

void SmokeInit(inout VFXAttributes attributes, in float3 origin, in float3 color)
{
    attributes.position = origin + (VFXRAND3 - 0.5) * 0.5;
    attributes.velocity = (VFXRAND3 - 0.5) * 1.0 + float3(0.0, 1.0 + VFXRAND * 0.6, 0.0);
    attributes.lifetime = 0.8 + VFXRAND * 0.4;
    attributes.size = 0.6 + VFXRAND * 0.4;
    attributes.angleZ = VFXRAND * 360.0;
    attributes.color = color;
}
