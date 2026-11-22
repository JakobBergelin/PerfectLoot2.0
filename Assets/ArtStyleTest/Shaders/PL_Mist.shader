// PerfectLoot mist layer (URP 17 / Unity 6): a sheet of drifting mist below the level (LevelBackdrop.Mist), in the
// abyss color lifted a little, cut into hard-edged comic bands. Stacked sheets at different depths drift at different
// speeds, so the drop below the level shows parallax as the camera moves. The noise is in world space (sheets of any
// size match) and fades out toward the sheet's UV border, so its edge never shows.
// Transparent, no shadows, SRP Batcher compatible, DOTS instancing.
Shader "PerfectLoot/Mist"
{
    Properties
    {
        _Lift ("Brightness (x abyss color)", Range(0.5,4)) = 1.7
        _Opacity ("Opacity", Range(0,1)) = 0.55
        _Scale ("Noise Scale (per m)", Range(0.005,0.5)) = 0.05
        _Coverage ("Coverage", Range(0,1)) = 0.45
        _Bands ("Bands", Range(1,6)) = 3
        _Drift ("Drift (m/s, xz)", Vector) = (0.6,0.25,0,0)
        _EdgeFade ("Edge Fade (UV)", Range(0.01,0.5)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-100" "IgnoreProjector"="True" }

        Pass
        {
            Name "ForwardMist"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PL_Abyss.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Lift;
                half _Opacity;
                float _Scale;
                half _Coverage;
                half _Bands;
                float4 _Drift;
                half _EdgeFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.uv = input.uv;
                return o;
            }

            float PLM_Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Smooth value noise, 0..1
            float PLM_Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(PLM_Hash(i), PLM_Hash(i + float2(1, 0)), u.x),
                            lerp(PLM_Hash(i + float2(0, 1)), PLM_Hash(i + float2(1, 1)), u.x), u.y);
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 p = (input.positionWS.xz - _Drift.xy * _Time.y) * _Scale;
                // Two octaves drifting against each other, so the shapes change as they move
                float n = PLM_Noise(p) * 0.65 + PLM_Noise(p * 2.3 + float2(_Time.y * 0.02, 17.3)) * 0.35;
                float density = saturate((n - _Coverage) / max(1.0 - _Coverage, 0.01));
                density = ceil(density * _Bands) / _Bands;
                // Fade out toward the sheet's border
                float2 edge = min(input.uv, 1.0 - input.uv);
                density *= smoothstep(0.0, _EdgeFade, min(edge.x, edge.y));
                half3 tint = _PL_AbyssDepth > 0.0 ? _PL_AbyssColor.rgb : unity_FogColor.rgb;
                return half4(tint * _Lift, density * _Opacity);
            }
            ENDHLSL
        }
    }
}
