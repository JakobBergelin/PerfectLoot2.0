#include "Assets/Data/Vfx/Graphs/HLSL/Fire/FireCommon.hlsl"

void EmberUpdate(inout VFXAttributes attributes, in float deltaTime)
{
    float t = FireLife(attributes.age, attributes.lifetime);
    attributes.velocity.y += 1.0 * deltaTime;
    attributes.scaleX = 1.0 - t; attributes.scaleY = 1.0 - t;
    attributes.color = FireBand(0.25 + 0.75 * t) * 3.0;
}
