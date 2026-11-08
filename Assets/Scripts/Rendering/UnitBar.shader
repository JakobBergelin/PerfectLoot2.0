// A unit's bar over its head (HealthUISystem, ResourceUISystem): a rounded pill with a thick dark outline, a dark inside
// and a flat fill with a lighter band along its top, in the style of the player HUD bars (UnitBarHUD). Drawn on a quad
// whose scale is the bar's size in meters, so the outline stays the same thickness on any bar. The colour comes from
// the unit's kind, so one material serves every bar.
// SRP Batcher compatible, supports DOTS instancing (Entities Graphics).
Shader "PerfectLoot/UnitBar"
{
    Properties
    {
        _EnemyColor ("Enemy", Color) = (0.91, 0.25, 0.24, 1)
        _CompanionColor ("Companion", Color) = (0.18, 0.83, 0.75, 1)
        _ResourceColor ("Resource", Color) = (0.24, 0.55, 1, 1)
        _BackColor ("Empty", Color) = (0.17, 0.14, 0.25, 0.9)
        _OutlineColor ("Outline", Color) = (0.07, 0.05, 0.11, 1)
        _OutlineWidth ("Outline Width (m)", Range(0, 0.15)) = 0.045
        _Highlight ("Top Band Brightness", Range(0, 1)) = 0.3
        // x: how full, 0 to 1. y: kind, 0 hidden, 1 enemy, 2 companion, 3 resource.
        _UnitBar ("Unit Bar", Vector) = (1, 1, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }

        Pass
        {
            Name "UnitBar"
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
                half4 _EnemyColor;
                half4 _CompanionColor;
                half4 _ResourceColor;
                half4 _BackColor;
                half4 _OutlineColor;
                float _OutlineWidth;
                float _Highlight;
                float4 _UnitBar;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
                UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                    UNITY_DOTS_INSTANCED_PROP(float4, _UnitBar)
                UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
                #define _UnitBar UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _UnitBar)
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
                float2 position : TEXCOORD0;    // Meters from the bar's centre
                float2 halfSize : TEXCOORD1;    // Half the bar's width and height in meters
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float4x4 m = GetObjectToWorldMatrix();
                output.halfSize = 0.5 * float2(length(m._m00_m10_m20), length(m._m01_m11_m21));
                output.position = (input.uv - 0.5) * 2.0 * output.halfSize;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 bar = _UnitBar;
                int kind = (int)round(bar.y);
                clip(kind - 0.5);
                half3 fillColor = kind == 1 ? _EnemyColor.rgb : kind == 2 ? _CompanionColor.rgb : _ResourceColor.rgb;

                // A pill: a rounded box whose ends are half circles. edge < 0 inside, in meters.
                float2 p = input.position;
                float2 halfSize = input.halfSize;
                float radius = halfSize.y;
                float edge = length(max(abs(p) - (halfSize - radius), 0.0)) - radius;
                float aa = max(fwidth(edge), 1e-5);
                float shape = 1.0 - smoothstep(-aa, aa, edge);
                clip(shape - 0.001);

                // The inside: filled from the left up to how full, the empty rest dark, a lighter band along the top
                float left = -halfSize.x + _OutlineWidth;
                float right = halfSize.x - _OutlineWidth;
                float fillEdge = lerp(left, right, saturate(bar.x)) - p.x;
                float filled = saturate(fillEdge / max(fwidth(p.x), 1e-5) + 0.5) * step(0.001, bar.x);
                float band = smoothstep(0.0, aa * 2.0, p.y - (halfSize.y - _OutlineWidth) * 0.15);
                half3 fill = lerp(fillColor, lerp(fillColor, 1.0, _Highlight), band);
                half4 inside = lerp(_BackColor, half4(fill, 1.0), filled);

                float outline = smoothstep(-aa, aa, edge + _OutlineWidth);
                half4 color = lerp(inside, _OutlineColor, outline);
                return half4(color.rgb, color.a * shape);
            }
            ENDHLSL
        }
    }
}
