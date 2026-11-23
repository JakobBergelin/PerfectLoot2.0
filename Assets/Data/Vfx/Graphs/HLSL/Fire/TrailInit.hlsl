#include "Assets/Data/Vfx/Graphs/HLSL/Fire/FireCommon.hlsl"

void TrailInit(inout VFXAttributes attributes, in float3 origin)
{
    attributes.position = origin;
    attributes.lifetime = 0.3;
    attributes.size = 0.55;
}
