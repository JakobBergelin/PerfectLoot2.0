#include "Assets/Data/Vfx/Graphs/HLSL/Ambient/AmbientCommon.hlsl"

void SplashInit(inout VFXAttributes attributes, in StructuredBuffer<float4> splashes, in uint splashCount, in float3 color)
{
    uint i = min((uint)(VFXRAND * splashCount), splashCount - 1);
    float3 flow = splashes[i * 2 + 1].xyz;
    attributes.alive = splashCount > 0;
    attributes.position = splashes[i * 2].xyz + float3(VFXRAND - 0.5, 0.15, VFXRAND - 0.5) * 1.4;
    attributes.velocity = float3(VFXRAND - 0.5, 0.0, VFXRAND - 0.5) * 1.2 + flow * 0.8 + float3(0.0, 1.6 + VFXRAND * 1.6, 0.0);
    attributes.lifetime = 0.45 + VFXRAND * 0.35;
    attributes.size = 0.1 + VFXRAND * 0.06;
    attributes.color = color;
}
