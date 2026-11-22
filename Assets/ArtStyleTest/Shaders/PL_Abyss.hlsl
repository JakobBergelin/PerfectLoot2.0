#ifndef PL_ABYSS_INCLUDED
#define PL_ABYSS_INCLUDED

// The abyss fade: everything below a level fades into one color, so the world falls away into the drop instead of
// stopping at its edge (LevelBackdrop). Global values set by LevelGenerationSystem while a level with a backdrop
// exists; _PL_AbyssDepth is 0 (off) otherwise. Applied after the distance fog, so deep down everything is exactly the
// abyss color, the same as the sky below the horizon (PL_FogSky).
half4 _PL_AbyssColor;
float _PL_AbyssTop;     // World height where the fade starts
float _PL_AbyssDepth;   // Meters below it where it's the abyss color (0 = off)
float _PL_AbyssBands;   // Hard steps (0 = smooth)

// 0 above _PL_AbyssTop, 1 at _PL_AbyssDepth meters below it, in hard steps
half PL_AbyssFade(float worldY)
{
    if (_PL_AbyssDepth <= 0.0) return 0.0h;
    half t = saturate((_PL_AbyssTop - worldY) / _PL_AbyssDepth);
    if (_PL_AbyssBands > 0.5) t = floor(t * _PL_AbyssBands + 0.001) / _PL_AbyssBands;
    return t;
}

half3 PL_ApplyAbyss(half3 color, float worldY)
{
    return lerp(color, _PL_AbyssColor.rgb, PL_AbyssFade(worldY));
}

#endif
