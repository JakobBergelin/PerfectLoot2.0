#ifndef PL_FIRE_COMMON
#define PL_FIRE_COMMON
// Fire in the comic style (art guide section 13): hard-edged stylized blobs, colored in hard bands along their life
// from a white-hot core to yellow, orange and a red fringe. Fireball.vfx and FireballImpact.vfx (FireGraphBuilder);
// VfxInstance hands them Origin (where the effect is) and GroundY (the level's ground) every frame.

// t: 0 just born .. 1 dying. Linear colors; glow multiplies them for the additive parts
float3 FireBand(float t)
{
    return t < 0.18 ? float3(1.0, 0.91, 0.72)       // #FFF6DC white-hot
         : t < 0.42 ? float3(1.0, 0.69, 0.14)       // #FFD86A yellow
         : t < 0.72 ? float3(1.0, 0.25, 0.024)      // #FF8A2A orange (the fire trait)
         : float3(0.58, 0.034, 0.012);              // #C8341C red fringe
}

float FireLife(float age, float lifetime) { return saturate(age / max(lifetime, 1e-4)); }
#endif
