// PerfectLoot chasm (URP 17 / Unity 6): the walls and floor of a bottomless drop. Color fades by depth below the rim (UV0.y, meters) from
// the rock at the rim to the biome's fog color deep down, in stepped bands, with slow mist drifting at the bottom.
// Follows the style guide's "platforms over a fog-filled drop". Opaque, SRP Batcher compatible, DOTS instancing.
Shader "PerfectLoot/Chasm"
{
    Properties
    {
        _RimColor ("Rim Color", Color) = (0.24,0.22,0.28,1)
        _FogColor ("Fog Color", Color) = (0.06,0.22,0.24,1)
        [HDR] _MistColor ("Mist Color (HDR)", Color) = (0.25,0.9,0.85,1)
        _Depth ("Fade Depth (m)", Range(0.5,30)) = 7
        _Bands ("Bands", Range(1,8)) = 4
        _MistAmount ("Mist Amount", Range(0,1)) = 0.45
        _MistSpeed ("Mist Speed", Range(0,2)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _RimColor;
            half4 _FogColor;
            half4 _MistColor;
            half _Depth;
            half _Bands;
            half _MistAmount;
            half _MistSpeed;
        CBUFFER_END

        float PLC_Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
        float PLC_Noise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(PLC_Hash(i), PLC_Hash(i + float2(1, 0)), f.x), lerp(PLC_Hash(i + float2(0, 1)), PLC_Hash(i + float2(1, 1)), f.x), f.y);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD2;
                float  fogFactor  : TEXCOORD1;
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
                float3 p = input.positionWS;
                float depth = saturate(input.uv.y / _Depth);   // UV0.y: meters below the rim
                // Mist: drifting noise that thickens toward the bottom and wobbles the band edges
                float mist = PLC_Noise(p.xz * 0.25 + float2(_Time.y * _MistSpeed, _Time.y * _MistSpeed * 0.6))
                           * 0.6 + PLC_Noise(p.xz * 0.6 - _Time.y * _MistSpeed * 1.3) * 0.4;
                float banded = floor(saturate(depth + (mist - 0.5) * 0.25) * _Bands) / max(_Bands - 1.0, 1.0);
                half3 color = lerp(_RimColor.rgb, _FogColor.rgb, saturate(banded));
                // Glowing mist at the bottom
                float glow = step(1.0 - _MistAmount, mist) * smoothstep(0.55, 1.0, depth);
                color = lerp(color, _MistColor.rgb, glow * 0.6);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings vert (Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }
            half4 frag (Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
