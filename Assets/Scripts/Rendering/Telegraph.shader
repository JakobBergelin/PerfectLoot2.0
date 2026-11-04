// The ground marker of a telegraphed enemy skill (TelegraphSystems.cs). Draws a circle, line or cone on a flat quad, with
// an outline that shows the whole area and a fill that grows until the skill goes off.
// SRP Batcher compatible, supports DOTS instancing (Entities Graphics).
Shader "PerfectLoot/Telegraph"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 0.15, 0.1, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.04
        _EmptyAlpha ("Alpha Before Filled", Range(0, 1)) = 0.12
        _FilledAlpha ("Alpha When Filled", Range(0, 1)) = 0.5
        // x: fill. y: shape (1 circle, 2 line, 3 cone). z: line half width or cone half angle in radians.
        _Telegraph ("Telegraph", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }

        Pass
        {
            Name "Telegraph"
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

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _OutlineWidth;
                float _EmptyAlpha;
                float _FilledAlpha;
                float4 _Telegraph;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
                UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                    UNITY_DOTS_INSTANCED_PROP(float4, _Telegraph)
                UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
                #define _Telegraph UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Telegraph)
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 telegraph = _Telegraph;
                float fill = telegraph.x;
                int shape = (int)round(telegraph.y);
                float2 p = input.uv;    // -1 to 1, 1 = Size; +y is forward
                float radius = length(p);

                // edge: how far inside the shape, negative outside. progress: 0 to 1 from where the fill starts.
                float edge = 1.0 - radius;
                float progress = radius;
                if (shape == 2)
                {
                    edge = min(min(p.y, 1.0 - p.y), telegraph.z - abs(p.x));
                    progress = p.y;
                }
                else if (shape == 3)
                {
                    float angle = atan2(abs(p.x), p.y);
                    edge = min(1.0 - radius, (telegraph.z - angle) * radius);
                }
                clip(edge);

                float alpha = progress <= fill ? _FilledAlpha : _EmptyAlpha;
                if (edge < _OutlineWidth) alpha = 1.0;
                return half4(_BaseColor.rgb, alpha * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
