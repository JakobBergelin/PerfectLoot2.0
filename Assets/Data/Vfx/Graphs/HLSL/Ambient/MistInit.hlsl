#include "Assets/Data/Vfx/Graphs/HLSL/Ambient/AmbientCommon.hlsl"

void MistInit(inout VFXAttributes attributes, in StructuredBuffer<float4> water, in uint waterCount, in float3 color)
{
    uint i = min((uint)(VFXRAND * waterCount), waterCount - 1);
    float3 jitter = float3(VFXRAND - 0.5, 0.0, VFXRAND - 0.5) * 3.0;
    attributes.alive = waterCount > 0;
    attributes.position = water[i * 2].xyz + jitter + float3(0.0, 0.25 + VFXRAND * 0.4, 0.0);
    attributes.velocity = water[i * 2 + 1].xyz * 0.35 + float3(VFXRAND - 0.5, 0.0, VFXRAND - 0.5) * 0.15;
    attributes.lifetime = 8.0 + VFXRAND * 5.0;
    attributes.size = 1.8 + VFXRAND * 1.4;
    attributes.color = color;
    attributes.angleZ = VFXRAND * 360.0;
}
