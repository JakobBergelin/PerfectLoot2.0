#ifndef PL_CANOPY_INCLUDED
#define PL_CANOPY_INCLUDED

// Sunlight through a canopy: leaf shade shaped like cartoon clouds and big drifting cloud shadows on everything below
// the treetops, plus the forest mask the height fog thickens with. Global values set by ForestAtmosphereFeature from the
// camera's volume (CanopyLight, HeightFog) and by LevelGenerationSystem (the level's ground height and forest mask);
// _PL_CanopyParams.x is 0 (off) without a CanopyLight volume. The leaf pattern hangs at the canopy height and is
// projected down along the sun, so the shade on the ground lines up with the light shafts in the fog. The leaf shade is
// its own lighter tone (PL_LeafShade, used by the shaders); cloud shadows only dim the sunlight.
float4 _PL_CanopyParams;    // x strength (how dark the leaf shade is), y 1 / puff size (per m), z coverage, w sway (m)
float4 _PL_CanopyShade;     // x hatching in the leaf shade, y edge softness (0 hard .. 1 soft)
float4 _PL_CanopyClouds;    // x 1 / cloud size (per m), y cloud coverage, zw cloud drift (m/s, xz)
float _PL_CanopyHeight;     // Meters above the ground where the leaves are
float _PL_GroundY;          // The level's ground height (world)

TEXTURE2D(_PL_ForestMask);  // 1 where the forest is (blurred), 0 in the open; black (no forest) without a level
SAMPLER(sampler_PL_ForestMask);
float4x4 _PL_ForestMaskMatrix;  // World position to the mask's uv (xz)

float PL_Hash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

// Smooth value noise, 0..1
float PL_ValueNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = PL_Hash(i), b = PL_Hash(i + float2(1, 0)), c = PL_Hash(i + float2(0, 1)), d = PL_Hash(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// Organic noise, 0..1 (mostly 0.25..0.75): three octaves of value noise, each turned a different way and the first
// bent a little, so the shapes are round blobs that don't line up with the level's grid (the fog's mist patches)
float PL_BlobNoise(float2 p)
{
    const float2x2 turn = float2x2(0.80, -0.60, 0.60, 0.80);
    p = mul(float2x2(0.92, -0.39, 0.39, 0.92), p);
    p += 0.35 * sin(p.yx * 1.3 + float2(1.7, 4.1));
    float n = PL_ValueNoise(p) * 0.55;
    p = mul(turn, p) * 2.03 + 3.1;
    n += PL_ValueNoise(p) * 0.3;
    p = mul(turn, p) * 2.01 + 1.7;
    return n + PL_ValueNoise(p) * 0.15;
}

// Cartoon cloud shapes, 0 outside, 1 inside (p in puffs, one puff per unit): one round puff per cell of a jittered
// grid, kept where a big slow noise is below the coverage, so the puffs clump into clouds with scalloped edges. Puffs
// get smaller toward a cloud's edge. softness is the width of the edge, in puffs.
half PL_Puffs(float2 p, float coverage, float softness)
{
    p = mul(float2x2(0.92, -0.39, 0.39, 0.92), p);      // Off the level's grid
    float2 i = floor(p);
    float inside = -1.0;                                 // How far inside the nearest puff's edge
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            float2 cell = i + float2(x, y);
            float2 center = cell + 0.5 + (float2(PL_Hash(cell), PL_Hash(cell + 17.3)) - 0.5) * 0.7;
            float clump = coverage - PL_ValueNoise(cell * 0.17 + 5.3);
            float radius = (0.6 + 0.3 * PL_Hash(cell + 41.7)) * saturate(clump * 3.0 + 0.65) * step(0.0, clump);
            inside = max(inside, radius - length(p - center));
        }
    float w = max(softness, 0.015);
    return smoothstep(-w, w, inside);
}

// 0 in the open, 1 deep in the forest
half PL_ForestAmount(float3 positionWS)
{
    float2 uv = mul(_PL_ForestMaskMatrix, float4(positionWS, 1.0)).xz;
    return SAMPLE_TEXTURE2D_LOD(_PL_ForestMask, sampler_PL_ForestMask, uv, 0).r;
}

// Where the sun's ray to this point passes the canopy height
float2 PL_CanopyPoint(float3 positionWS)
{
    float3 toSun = _MainLightPosition.xyz;
    return positionWS.xz + toSun.xz * (_PL_GroundY + _PL_CanopyHeight - positionWS.y) / max(toSun.y, 0.2);
}

// Leaf shade: 1 under the leaves, 0 in the sun (the shape only; the shaders scale it by the strength). Treetops sit at
// the canopy, so they get none.
half PL_LeafShade(float3 positionWS)
{
    if (_PL_CanopyParams.x <= 0.0) return 0.0h;
    float2 p = PL_CanopyPoint(positionWS);
    // Puffs that rock a little in the wind
    float2 sway = float2(sin(_Time.y * 0.9 + p.y * 0.05), cos(_Time.y * 0.7 + p.x * 0.05)) * _PL_CanopyParams.w;
    half shade = PL_Puffs((p + sway) * _PL_CanopyParams.y, _PL_CanopyParams.z, _PL_CanopyShade.y * 0.3);
    float canopyY = _PL_GroundY + _PL_CanopyHeight;
    return shade * (1.0h - smoothstep(canopyY - 2.0, canopyY, positionWS.y));
}

// Sun through the leaves: 1 in the sun, 0 under the leaves (the fog's light shafts)
half PL_CanopyLight(float3 positionWS)
{
    return 1.0h - PL_LeafShade(positionWS);
}

// Cloud shadow: big puffy clouds drifting one way that dim the sunlight (1 in the open, down to 0.7 under a cloud),
// so a passing cloud never turns the whole view to hatching
half PL_CloudLight(float3 positionWS)
{
    if (_PL_CanopyParams.x <= 0.0) return 1.0h;
    float2 c = (PL_CanopyPoint(positionWS) - _PL_CanopyClouds.zw * _Time.y) * _PL_CanopyClouds.x;
    return lerp(1.0h, 0.7h, PL_Puffs(c + 31.0, _PL_CanopyClouds.y, 0.02 + _PL_CanopyShade.y * 0.1));
}

#endif
