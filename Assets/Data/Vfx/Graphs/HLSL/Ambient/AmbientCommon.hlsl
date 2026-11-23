#ifndef PL_AMBIENT_COMMON
#define PL_AMBIENT_COMMON
// The level's air and water (LevelAmbientGraph, built by AmbientGraphBuilder): fireflies in the forest, pollen over the
// clearings, leaves falling at the forest's edge, mist drifting over the water and splashes where rivers pass bridges
// and rocks. LevelAmbientGraphView hands every system the level's bounds, forest mask (0 open, 1 forest), ground height
// and point lists (two float4 each: world position, world flow).


float AmbientForest(VFXSampler2D forest, float4x4 forestMatrix, float3 position)
{
    return SampleTexture(forest, mul(forestMatrix, float4(position, 1.0)).xz, 0).r;
}


float AmbientGround(VFXSampler2D ground, float4x4 groundMatrix, float3 position)
{
    return SampleTexture(ground, mul(groundMatrix, float4(position, 1.0)).xz, 0).r;
}


float3 AmbientSpot(float3 center, float3 size, float2 r)
{
    return float3(center.x + (r.x - 0.5) * size.x / 1.5, 0.0, center.z + (r.y - 0.5) * size.z / 1.5);
}


// Fades in over the first part of its life and out over the last
float AmbientFade(float age, float lifetime, float fadeIn, float fadeOut)
{
    return saturate(age / fadeIn) * saturate((lifetime - age) / fadeOut);
}

#endif
