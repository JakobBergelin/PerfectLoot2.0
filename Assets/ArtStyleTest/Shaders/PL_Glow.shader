// PerfectLoot light pool (URP 17 / Unity 6): an additive glow lying on the floor around lava, glowing water or a lit
// chasm, so liquids seem to light the ground without real point lights (PerfectLoot/Comic's point light pools have a
// short fixed reach). UV0.x is the distance from the source edge in meters (0 at the edge), written by
// BiomeTileGenerator. The glow falls off in stepped bands to match the comic shading, and flickers gently.
// Transparent, no shadows, SRP Batcher compatible, DOTS instancing.
Shader "PerfectLoot/Glow"
{
    Properties
    {
        [HDR] _Color ("Glow Color (HDR)", Color) = (1.2,0.35,0.1,1)
        _Reach ("Reach (m)", Range(0.1,6)) = 1.6
        _Bands ("Bands", Range(1,8)) = 3
        _Flicker ("Flicker", Range(0,1)) = 0.25
        _FlickerSpeed ("Flicker Speed", Range(0,10)) = 2.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-50" "IgnoreProjector"="True" }

        Pass
        {
            Name "ForwardGlow"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Reach;
                half _Bands;
                half _Flicker;
                half _FlickerSpeed;
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
                float  fogFactor  : TEXCOORD2;
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
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float d = saturate(input.uv.x / max(_Reach, 0.01));
                float falloff = (1.0 - d) * (1.0 - d);
                // Stepped bands, like the comic light pools
                falloff = ceil(falloff * _Bands) / _Bands * step(0.001, falloff);
                float2 p = input.positionWS.xz;
                float flicker = 1.0 - _Flicker * (0.5 + 0.5 * sin(_Time.y * _FlickerSpeed + p.x * 0.9 + p.y * 1.3)) * (0.5 + 0.5 * sin(_Time.y * _FlickerSpeed * 1.7 + p.y * 0.6));
                half3 color = _Color.rgb * falloff * flicker;
                // Fog fades the glow away rather than tinting it
                color *= ComputeFogIntensity(input.fogFactor);
                return half4(color, 0.0h);
            }
            ENDHLSL
        }
    }
}
