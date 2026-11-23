#include "Assets/Data/Vfx/Graphs/HLSL/Fire/FireCommon.hlsl"

// Falls and bounces off the ground, losing speed each time
void SparkUpdate(inout VFXAttributes attributes, in float groundY, in float deltaTime)
{
    float t = FireLife(attributes.age, attributes.lifetime);
    attributes.velocity.y -= 14.0 * deltaTime;
    if (attributes.position.y < groundY + 0.05 && attributes.velocity.y < 0.0)
    {
        attributes.position.y = groundY + 0.05;
        attributes.velocity.y *= -0.45;
        attributes.velocity.xz *= 0.7;
    }
    attributes.color = FireBand(0.2 + 0.7 * t) * 3.0;
}
