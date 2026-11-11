// PerfectLoot liquid surface (URP 17 / Unity 6): water, swamp and lava pools of the biome tile sets.
// Comic look: flat color bands instead of realistic shading, stepped foam at the shore, stylized ripple lines.
// UV0.x is the distance to the shore in meters (written by BiomeTileGenerator), so the foam and the shallow band
// survive the level's mesh merging (which keeps only UV0). Patterns use world position, so pools are seamless.
// Opaque, SRP Batcher compatible, DOTS instancing (Entities Graphics renders the level chunks).
Shader "PerfectLoot/Liquid"
{
    Properties
    {
        [KeywordEnum(Water, Lava)] _Mode ("Mode", Float) = 0
        _DeepColor ("Deep Color", Color) = (0.08,0.2,0.24,1)
        _ShallowColor ("Shallow Color", Color) = (0.12,0.32,0.34,1)
        _ShallowWidth ("Shallow Band (m)", Range(0,3)) = 0.9
        _FoamColor ("Foam / Shore Color", Color) = (0.55,0.85,0.8,1)
        _FoamWidth ("Foam Width (m)", Range(0,1)) = 0.18
        _RippleColor ("Ripple Line Color", Color) = (0.3,0.6,0.6,1)
        _RippleScale ("Ripple Scale", Range(0.05,2)) = 0.45
        _RippleWidth ("Ripple Line Width", Range(0,0.3)) = 0.06
        _FlowSpeed ("Flow Speed", Range(0,2)) = 0.25
        [HDR] _GlowColor ("Lava Glow (HDR)", Color) = (4,0.9,0.2,1)
        _CrustColor ("Lava Crust Color", Color) = (0.16,0.06,0.07,1)
        _CrustAmount ("Lava Crust Amount", Range(0,1)) = 0.45
        _PulseSpeed ("Lava Pulse Speed", Range(0,5)) = 0.8
        _Brightness ("Brightness", Range(0,3)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor;
            half4 _ShallowColor;
            half _ShallowWidth;
            half4 _FoamColor;
            half _FoamWidth;
            half4 _RippleColor;
            half _RippleScale;
            half _RippleWidth;
            half _FlowSpeed;
            half4 _GlowColor;
            half4 _CrustColor;
            half _CrustAmount;
            half _PulseSpeed;
            half _Brightness;
        CBUFFER_END

        float2 PLL_Hash2(float2 p)
        {
            p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
            return frac(sin(p) * 43758.5453);
        }

        float PLL_Noise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = PLL_Hash2(i).x, b = PLL_Hash2(i + float2(1, 0)).x;
            float c = PLL_Hash2(i + float2(0, 1)).x, d = PLL_Hash2(i + float2(1, 1)).x;
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        // Distance to the nearest cell edge (F2 - F1) and the cell's random id, for lava crust plates
        float2 PLL_Cells(float2 p, float t)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            float f1 = 8.0, f2 = 8.0, id = 0.0;
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                float2 g = float2(x, y);
                float2 o = PLL_Hash2(i + g);
                o = 0.5 + 0.35 * sin(t + 6.2831 * o);
                float d = length(g + o - f);
                if (d < f1) { f2 = f1; f1 = d; id = PLL_Hash2(i + g + 7.0).x; }
                else if (d < f2) f2 = d;
            }
            return float2(f2 - f1, id);
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
            #pragma shader_feature_local _MODE_WATER _MODE_LAVA
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

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
                float shore = input.uv.x;                       // meters from the shore
                float2 p = input.positionWS.xz;
                float t = _Time.y * _FlowSpeed;
                // The shore line wobbles a little so the foam isn't a perfect offset of the bank
                float wobble = (PLL_Noise(p * 1.7 + t * 0.6) - 0.5) * 0.12;
                half3 color;

            #if defined(_MODE_LAVA)
                float2 cells = PLL_Cells(p * 0.55 + float2(t * 0.15, t * 0.05), t * 0.6);
                float crustNoise = PLL_Noise(p * 0.35 - t * 0.1);
                // Crust plates float in the middle; the shore and the seams between plates glow
                float crust = step(1.0 - _CrustAmount, crustNoise * 0.6 + cells.y * 0.4) * step(0.12, cells.x);
                crust *= step(_FoamWidth * 2.5, shore + wobble);
                float pulse = 0.85 + 0.15 * sin(_Time.y * _PulseSpeed + crustNoise * 6.2831);
                half3 hot = _GlowColor.rgb * pulse;
                half3 warm = lerp(_DeepColor.rgb, hot, 0.55);
                // Three stepped bands: white-hot seams, hot lava, darker cooling lava
                float heat = saturate(1.0 - cells.x * 2.2) * 0.6 + PLL_Noise(p * 1.3 + t) * 0.4;
                color = heat > 0.66 ? hot * 1.15 : (heat > 0.4 ? hot * 0.8 : warm);
                color = lerp(color, _CrustColor.rgb, crust);
                // The bank edge burns brightest
                color = lerp(color, _GlowColor.rgb * 1.2, step(shore + wobble, _FoamWidth));
            #else
                // Stepped depth: shallow band at the shore, deep in the middle
                color = shore + wobble < _ShallowWidth ? _ShallowColor.rgb : _DeepColor.rgb;
                // Ripple lines: the zero-crossings of two drifting noise fields, as thin flat strokes
                float n1 = PLL_Noise(p * _RippleScale + float2(t, t * 0.4));
                float n2 = PLL_Noise(p * _RippleScale * 1.7 - float2(t * 0.6, t * 0.9) + 13.1);
                float ripple = abs(n1 + n2 - 1.0);
                color = lerp(color, _RippleColor.rgb, step(ripple, _RippleWidth) * step(_FoamWidth * 2.0, shore));
                // Foam hugging the bank, with a second thinner ring that breathes in and out
                float ring = _FoamWidth * (1.6 + 0.4 * sin(_Time.y * 1.3 + p.x * 0.7 + p.y * 0.5));
                float foam = step(shore + wobble, _FoamWidth) + step(abs(shore + wobble - ring), _FoamWidth * 0.22);
                color = lerp(color, _FoamColor.rgb, saturate(foam));
            #endif

                color *= _Brightness;
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
