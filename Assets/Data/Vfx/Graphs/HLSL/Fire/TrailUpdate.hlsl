#include "Assets/Data/Vfx/Graphs/HLSL/Fire/FireCommon.hlsl"

void TrailUpdate(inout VFXAttributes attributes)
{
    float t = FireLife(attributes.age, attributes.lifetime);
    attributes.size = 0.55 * (1.0 - t);
    attributes.color = FireBand(t) * 2.0;
}
