#include "Assets/Data/Vfx/Graphs/HLSL/Ambient/AmbientCommon.hlsl"

void FireflyUpdate(inout VFXAttributes attributes)
{
    float t = attributes.age, p = attributes.texIndex;
    // A lazy wander, and a blink every couple of seconds
    attributes.velocity = float3(sin(t * 0.7 + p) * 0.45, sin(t * 1.3 + p * 2.0) * 0.18, cos(t * 0.6 + p * 3.0) * 0.45);
    float blink = saturate(sin(t * 2.1 + p) * 2.5 - 0.6);
    attributes.alpha = blink * AmbientFade(attributes.age, attributes.lifetime, 1.0, 1.0);
}
