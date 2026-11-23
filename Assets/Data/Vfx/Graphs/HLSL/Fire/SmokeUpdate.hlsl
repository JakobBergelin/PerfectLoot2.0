#include "Assets/Data/Vfx/Graphs/HLSL/Fire/FireCommon.hlsl"

// Rises and puffs up, then shrinks away (hard edges: it never fades)
void SmokeUpdate(inout VFXAttributes attributes, in float deltaTime)
{
    float t = FireLife(attributes.age, attributes.lifetime);
    attributes.velocity *= exp(-2.0 * deltaTime);
    float s = (0.6 + 0.7 * min(t * 2.0, 1.0)) * (1.0 - smoothstep(0.6, 1.0, t));
    attributes.scaleX = s; attributes.scaleY = s;
}
