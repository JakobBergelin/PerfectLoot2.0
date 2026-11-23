#include "Assets/Data/Vfx/Graphs/HLSL/Fire/FireCommon.hlsl"

void SparkInit(inout VFXAttributes attributes, in float3 origin)
{
    float3 dir = normalize(VFXRAND3 - 0.5);
    dir.y = abs(dir.y) + 0.3;
    attributes.position = origin;
    attributes.velocity = normalize(dir) * (5.0 + VFXRAND * 4.0);
    attributes.lifetime = 0.45 + VFXRAND * 0.3;
    attributes.size = 0.07 + VFXRAND * 0.04;
}
