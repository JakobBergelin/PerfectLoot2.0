using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How effects stir the height fog's mist (MistWake): VfxManager stirs it every frame around each shown effect with a rule
/// here, effects that follow something (a fireball, a tornado) while they're shown and one-shots (an impact, a footstep)
/// for their first Burst seconds. Since every machine shows the same effects, every machine's mist moves the same.
/// Push: m/s outward (negative pulls in); Swirl: m/s around; Clear: how fast it thins (per second). Bending foliage
/// (bushes, grass tufts) leans with the pushed mist (PL_Foliage.hlsl), so a rule here moves them too.
/// </summary>
public static class VfxMist
{
    public struct Rule
    {
        public float Radius, Push, Swirl, Clear;
        public float Burst;     // One-shots: seconds they stir after they start
    }

    static Rule R(float radius, float push, float swirl = 0f, float clear = 0f, float burst = 0.3f) =>
        new Rule { Radius = radius, Push = push, Swirl = swirl, Clear = clear, Burst = burst };

    public static readonly Dictionary<VfxType, Rule> Rules = new Dictionary<VfxType, Rule>
    {
        // Fire burns a tunnel through it and blows it away where it lands
        [VfxType.FireballOrb] = R(2.5f, 5f, clear: 0.5f),
        [VfxType.FireballImpact] = R(5f, 14f, clear: 0.9f, burst: 0.35f),
        [VfxType.Flame] = R(3f, 6f, clear: 0.6f),
        [VfxType.BurningGround] = R(3f, 0f, clear: 0.5f),
        // Wind turns it around: tornadoes draw it in as they spin, a whirlwind throws it around
        [VfxType.Tornado] = R(4f, -3f, swirl: 9f, clear: 0.3f),
        [VfxType.TornadoEnd] = R(4f, 8f),
        [VfxType.WhirlwindArc] = R(3.5f, 2f, swirl: 10f, clear: 0.3f),
        [VfxType.FrozenOrb] = R(2f, 4f, swirl: 3f),
        // Players: dashes shove it aside, footsteps leave a trail
        [VfxType.DashTrail] = R(2f, 7f, clear: 0.4f),
        [VfxType.DashBurst] = R(3f, 10f, burst: 0.25f),
        [VfxType.FootstepDust] = R(1.1f, 3f, clear: 0.25f),
        // Undertow pulls it in to where it drags its enemy
        [VfxType.UndertowRipple] = R(3.5f, -8f, swirl: 3f, burst: 0.5f),
        // Blasts and slams
        [VfxType.StrikeShock] = R(3f, 9f, burst: 0.25f),
        [VfxType.ColdNova] = R(6f, 12f, clear: 0.6f, burst: 0.35f),
        [VfxType.MeteorImpact] = R(6f, 16f, clear: 0.9f, burst: 0.35f),
        [VfxType.UpheavalSlam] = R(5f, 12f, burst: 0.3f),
        [VfxType.UpheavalQuake] = R(5f, 12f, burst: 0.3f),
        [VfxType.LightningHit] = R(1.5f, 4f, burst: 0.2f),
    };
}
