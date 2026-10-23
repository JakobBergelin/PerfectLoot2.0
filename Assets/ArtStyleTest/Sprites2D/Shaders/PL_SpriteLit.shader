// PerfectLoot lit sprite for 3D scenes (URP 17 / Unity 6)
// - alpha-cutout sprites that write depth (no sorting problems in a 3D world)
// - receive the main light (with shadows) and point lights (lava, braziers), cast shadows
// - "glow" pixels (bright, saturated fire/gold colors) are treated as emissive so they bloom
// Works with SpriteRenderer (uses _MainTex + vertex color) and with quads.
Shader "PerfectLoot/SpriteLit"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _Ambient ("Ambient Strength", Range(0,2)) = 0.55
        _LightInfluence ("Main Light Influence", Range(0,2)) = 0.75
        _PointInfluence ("Point Light Influence", Range(0,3)) = 1.2
        _ShadowStrength ("Received Shadow Strength", Range(0,1)) = 0.7
        _GlowBoost ("Glow Boost (HDR)", Range(0,8)) = 2.5
        _GlowMinSat ("Glow Min Saturation", Range(0,1)) = 0.6
        _GlowMinVal ("Glow Min Brightness", Range(0,1)) = 0.82
        _GlowMaxHue ("Glow Max Hue (0-1, warm range)", Range(0,0.25)) = 0.17
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Cutoff;
            half _Ambient;
            half _LightInfluence;
            half _PointInfluence;
            half _ShadowStrength;
            half _GlowBoost;
            half _GlowMinSat;
            half _GlowMinVal;
            half _GlowMaxHue;
        CBUFFER_END
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.uv = v.uv; o.color = v.color * _Color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half3 RgbToHsv(half3 c)
            {
                half4 K = half4(0.0h, -1.0h / 3.0h, 2.0h / 3.0h, -1.0h);
                half4 p = lerp(half4(c.bg, K.wz), half4(c.gb, K.xy), step(c.b, c.g));
                half4 q = lerp(half4(p.xyw, c.r), half4(c.r, p.yzx), step(p.x, c.r));
                half d = q.x - min(q.w, q.y);
                return half3(abs(q.z + (q.w - q.y) / (6.0h * d + 1e-4h)), d / (q.x + 1e-4h), q.x);
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
                clip(tex.a - _Cutoff);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                half4 shadowMask = half4(1,1,1,1);

                // sprites have baked shading; scene lights only tint them
                Light mainLight = GetMainLight(inputData.shadowCoord, i.positionWS, shadowMask);
                half shadow = lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength);
                half3 light = SampleSH(half3(0,1,0)) * _Ambient + mainLight.color * shadow * _LightInfluence;

                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, shadowMask);
                    light += l.color * l.distanceAttenuation * _PointInfluence;
                LIGHT_LOOP_END
                #endif

                half3 color = tex.rgb * light;

                // emissive heuristic for fire / gold / glowing eyes
                half3 hsv = RgbToHsv(tex.rgb);
                half warm = step(hsv.x, _GlowMaxHue) + step(0.97h, hsv.x);
                half glow = saturate(warm) * step(_GlowMinSat, hsv.y) * step(_GlowMinVal, hsv.z);
                color = lerp(color, tex.rgb * _GlowBoost, glow);

                color = MixFog(color, i.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half a : TEXCOORD1; };
            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, lightDir, lightDir));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS; o.uv = v.uv; o.a = v.color.a * _Color.a;
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.a - _Cutoff);
                return 0;
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
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half a : TEXCOORD1; };
            Varyings vert (Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; o.a = v.color.a * _Color.a; return o; }
            half frag (Varyings i) : SV_Target { clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.a - _Cutoff); return i.positionCS.z; }
            ENDHLSL
        }
    }
    FallBack Off
}
