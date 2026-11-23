#include "Assets/Data/Vfx/Graphs/HLSL/Ambient/AmbientCommon.hlsl"

void MistUpdate(inout VFXAttributes attributes, in float deltaTime)
{
    attributes.size += 0.12 * deltaTime;
    attributes.angleZ += 4.0 * deltaTime;
    attributes.alpha = 0.22 * AmbientFade(attributes.age, attributes.lifetime, 2.5, 3.0);
}
