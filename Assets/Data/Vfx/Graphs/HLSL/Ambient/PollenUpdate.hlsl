#include "Assets/Data/Vfx/Graphs/HLSL/Ambient/AmbientCommon.hlsl"

void PollenUpdate(inout VFXAttributes attributes)
{
    float t = attributes.age, p = attributes.texIndex;
    attributes.velocity = float3(0.3 + sin(t * 0.5 + p) * 0.15, sin(t * 0.9 + p) * 0.08, 0.12 + cos(t * 0.4 + p) * 0.15);
    attributes.alpha = 0.8 * AmbientFade(attributes.age, attributes.lifetime, 1.5, 2.0);
}
