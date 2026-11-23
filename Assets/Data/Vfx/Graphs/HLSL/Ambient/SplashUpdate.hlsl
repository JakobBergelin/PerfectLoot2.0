#include "Assets/Data/Vfx/Graphs/HLSL/Ambient/AmbientCommon.hlsl"

void SplashUpdate(inout VFXAttributes attributes, in float deltaTime)
{
    attributes.velocity.y -= 9.8 * deltaTime;
    attributes.alpha = AmbientFade(attributes.age, attributes.lifetime, 0.05, 0.2);
}
