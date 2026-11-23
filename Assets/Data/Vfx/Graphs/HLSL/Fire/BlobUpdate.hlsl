#include "Assets/Data/Vfx/Graphs/HLSL/Fire/FireCommon.hlsl"

// Shrinks away, stepping through the fire bands
void BlobUpdate(inout VFXAttributes attributes)
{
    float t = FireLife(attributes.age, attributes.lifetime);
    float s = 1.0 - t * t;
    attributes.scaleX = s; attributes.scaleY = s;
    attributes.color = FireBand(t);
}
