// PerfectLoot height fog (URP 17 / Unity 6): low mist over the level's ground, drawn by ForestAtmosphereFeature as one
// full-screen pass over the opaques (before transparents). Rebuilds each pixel's world position from depth and walks
// the view ray through the mist layer in a few steps: the mist reaches higher and is thicker toward the forest (the
// level's forest mask), drifts in patches, and glows where the sun falls through gaps in the canopy (light shafts,
// PL_Canopy.hlsl). The result is cut into hard comic bands. Settings come from the HeightFog volume (global values).
Shader "Hidden/PerfectLoot/HeightFog"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "HeightFog"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "PL_Canopy.hlsl"

            half4 _PL_FogColor;
            float4 _PL_FogParams;   // x intensity, y density, z bands, w shaft strength
            float4 _PL_FogShape;    // x height in the open, y height in the forest, z 1 / patch size, w forest density factor
            float2 _Drift;          // Patch drift (m/s, xz)

            #define FOG_STEPS 8

            half4 frag (Varyings input) : SV_Target
            {
                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                float depth = SampleSceneDepth(uv);
            #if !UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
            #endif
                float3 positionWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                // The part of the view ray inside the mist layer (a little below the ground for dips, up to its highest top)
                float3 cam = _WorldSpaceCameraPos;
                float3 ray = positionWS - cam;
                float ground = _PL_GroundY;
                float top = ground + max(_PL_FogShape.x, _PL_FogShape.y), bottom = ground - 1.5;
                float t0 = 0.0, t1 = 1.0;
                if (abs(ray.y) > 1e-4)
                {
                    float ta = (top - cam.y) / ray.y, tb = (bottom - cam.y) / ray.y;
                    t0 = max(0.0, min(ta, tb));
                    t1 = min(1.0, max(ta, tb));
                }
                else if (cam.y > top || cam.y < bottom) return 0;
                if (t1 <= t0) return 0;

                float stepLength = length(ray) * (t1 - t0) / FOG_STEPS;
                float2 drift = _Drift * _Time.y;
                float optical = 0.0, lit = 0.0;
                [unroll] for (int i = 0; i < FOG_STEPS; i++)
                {
                    float3 p = cam + ray * lerp(t0, t1, (i + 0.5) / FOG_STEPS);
                    float forest = PL_ForestAmount(p);
                    float localTop = ground + lerp(_PL_FogShape.x, _PL_FogShape.y, forest);
                    float below = localTop - p.y;
                    if (below <= 0.0) continue;
                    // Thickest at the ground, thinning toward the top; in drifting patches; thicker in the forest
                    float dens = saturate(below / max(localTop - ground, 0.3));
                    float2 q = (p.xz - drift) * _PL_FogShape.z;
                    float patches = PL_BlobNoise(q + 11.0);
                    dens *= saturate((patches - 0.42) * 4.0) * lerp(1.0, _PL_FogShape.w, forest);
                    optical += dens * stepLength;
                    lit += dens * stepLength * PL_CanopyLight(p);
                }

                float amount = 1.0 - exp(-optical * _PL_FogParams.y);
                // Hard steps, rounded down: a thin veil vanishes instead of washing over the open ground
                if (_PL_FogParams.z > 0.5) amount = floor(amount * _PL_FogParams.z) / _PL_FogParams.z;
                // Light shafts: the share of the mist the sun reaches (only with canopy light; without it, no shafts)
                half shaft = smoothstep(0.45, 0.6, lit / max(optical, 1e-4)) * step(1e-4, _PL_CanopyParams.x);
                half3 color = _PL_FogColor.rgb + _MainLightColor.rgb * shaft * _PL_FogParams.w * 0.35h;
                return half4(color, amount * _PL_FogParams.x);
            }
            ENDHLSL
        }
    }
}
