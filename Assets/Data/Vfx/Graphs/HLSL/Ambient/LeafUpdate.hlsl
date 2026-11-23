#include "Assets/Data/Vfx/Graphs/HLSL/Ambient/AmbientCommon.hlsl"

void LeafUpdate(inout VFXAttributes attributes, in VFXSampler2D ground, in float4x4 groundMatrix, in float deltaTime)
{
    float t = attributes.age, p = attributes.texIndex;
    float rest = AmbientGround(ground, groundMatrix, attributes.position) + 0.04;
    if (attributes.position.y > rest)
    {
        // Flutter: rock side to side as it falls, turning
        attributes.velocity = float3(sin(t * 2.2 + p) * 0.9 + 0.25, -0.55 - 0.25 * sin(t * 4.4 + p), cos(t * 1.7 + p) * 0.45 + 0.1);
        attributes.angleZ += cos(t * 2.2 + p) * 160.0 * deltaTime;
    }
    else
    {
        attributes.position.y = rest;
        attributes.velocity = 0;
    }
    attributes.alpha = AmbientFade(attributes.age, attributes.lifetime, 0.5, 3.0);
}
