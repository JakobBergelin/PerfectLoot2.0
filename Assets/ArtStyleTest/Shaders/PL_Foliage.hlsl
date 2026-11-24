#ifndef PL_FOLIAGE_INCLUDED
#define PL_FOLIAGE_INCLUDED

// Foliage that bends out of the way: bushes and flowers lean away from players walking through them, and skills push
// them the same way they push the mist (the mist wake, MistWake.cs), so a fireball's blast or a whirlwind bends them too
// and they slowly straighten as the mist refills. Players come from FoliagePushSystem (_PL_Pushers); the wake is black
// (no push) without a level. Returns the sideways offset in meters (world xz), before the material's own strength.
#define PL_MAX_PUSHERS 8
float4 _PL_Pushers[PL_MAX_PUSHERS];     // xyz position, w radius (m)
int _PL_PusherCount;

TEXTURE2D(_PL_MistWake);                // xy: how far the mist was pushed (m, world xz)
SAMPLER(sampler_PL_MistWake);
float4x4 _PL_MistWakeMatrix;

static const float PL_PlayerLean = 0.45;    // Meters a bush leans right next to a player
static const float PL_WakeLean = 0.12;      // Lean per meter the mist was pushed
static const float PL_MaxLean = 0.6;

float2 PL_FoliagePush(float3 positionWS)
{
    float2 push = 0.0;
    for (int i = 0; i < _PL_PusherCount; i++)
    {
        float2 d = positionWS.xz - _PL_Pushers[i].xz;
        float distance = length(d);
        float f = saturate(1.0 - distance / _PL_Pushers[i].w);
        push += (distance > 1e-3 ? d / distance : 0.0) * f * f * PL_PlayerLean;
    }
    push += SAMPLE_TEXTURE2D_LOD(_PL_MistWake, sampler_PL_MistWake, mul(_PL_MistWakeMatrix, float4(positionWS, 1.0)).xz, 0).xy
          * PL_WakeLean;
    float length2 = dot(push, push);
    return length2 > PL_MaxLean * PL_MaxLean ? push * (PL_MaxLean * rsqrt(length2)) : push;
}

#endif
