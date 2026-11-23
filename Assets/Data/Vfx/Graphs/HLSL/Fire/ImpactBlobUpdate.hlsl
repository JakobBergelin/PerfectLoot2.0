#include "Assets/Data/Vfx/Graphs/HLSL/Fire/FireCommon.hlsl"

// Puffs up, then shrinks away through the fire bands, slowing down
void ImpactBlobUpdate(inout VFXAttributes attributes, in float deltaTime)
{
    float t = FireLife(attributes.age, attributes.lifetime);
    attributes.velocity *= exp(-6.0 * deltaTime);
    float s = sin(3.14159 * min(t * 1.6, 1.0)) * 0.6 + (1.0 - t) * 0.6;
    attributes.scaleX = s; attributes.scaleY = s;
    attributes.color = FireBand(t);
}
