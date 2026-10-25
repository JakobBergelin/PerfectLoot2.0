// PerfectLoot comic-book dark fantasy shader (URP 17 / Unity 6)
// - flat 2-tone color, shadows filled with screen-space ink hatching or halftone dots
// - cross-hatching in the deepest shadow
// - hard comic rim light, pulsing trait glow
// - hand-inked outline: width varies along the surface
// SRP Batcher compatible, supports DOTS instancing (Entities Graphics).
Shader "PerfectLoot/Comic"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)

        [Header(Two Tone)]
        _ShadowColor ("Shadow Tint", Color) = (0.45,0.38,0.55,1)
        _ShadeThreshold ("Shade Threshold", Range(0,1)) = 0.55
        _DeepThreshold ("Deep Shadow Threshold", Range(0,1)) = 0.25
        _AmbientStrength ("Ambient Strength", Range(0,2)) = 0.35
        _ShadowReceive ("Receive Shadows", Range(0,1)) = 1

        [Header(Ink Pattern)]
        [KeywordEnum(Hatch, Halftone)] _Pattern ("Shadow Pattern", Float) = 0
        _InkColor ("Ink Color", Color) = (0.07,0.05,0.08,1)
        _PatternScale ("Pattern Spacing (px)", Range(2,24)) = 7
        _PatternAngle ("Hatch Angle (deg)", Range(0,180)) = 35
        _PatternStrength ("Pattern Strength", Range(0,1)) = 0.85

        [Header(Rim)]
        [HDR] _RimColor ("Rim Color", Color) = (1,0.85,0.7,1)
        _RimPower ("Rim Width", Range(0.01,1)) = 0.28
        _RimStrength ("Rim Strength", Range(0,2)) = 0.6

        [Header(Trait Glow)]
        _EmissionMap ("Emission Mask", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _EmissionPulse ("Pulse Amount", Range(0,1)) = 0
        _EmissionPulseSpeed ("Pulse Speed", Range(0,10)) = 2

        [Header(Ink Outline)]
        _OutlineColor ("Outline Color", Color) = (0.05,0.04,0.06,1)
        _OutlineWidth ("Outline Width (px)", Range(0,10)) = 3.5
        _OutlineJitter ("Line Weight Variation", Range(0,1)) = 0.5
        [Toggle] _UseSmoothNormals ("Outline Uses Smoothed Normals (UV3)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadowColor;
            half _ShadeThreshold;
            half _DeepThreshold;
            half _AmbientStrength;
            half _ShadowReceive;
            half4 _InkColor;
            half _PatternScale;
            half _PatternAngle;
            half _PatternStrength;
            half4 _RimColor;
            half _RimPower;
            half _RimStrength;
            float4 _EmissionMap_ST;
            half4 _EmissionColor;
            half _EmissionPulse;
            half _EmissionPulseSpeed;
            half4 _OutlineColor;
            half _OutlineWidth;
            half _OutlineJitter;
            half _UseSmoothNormals;
        CBUFFER_END

        TEXTURE2D(_BaseMap);     SAMPLER(sampler_BaseMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

        float PL_Hash(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float PL_Noise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(PL_Hash(i + float3(0,0,0)), PL_Hash(i + float3(1,0,0)), f.x),
                             lerp(PL_Hash(i + float3(0,1,0)), PL_Hash(i + float3(1,1,0)), f.x), f.y),
                        lerp(lerp(PL_Hash(i + float3(0,0,1)), PL_Hash(i + float3(1,0,1)), f.x),
                             lerp(PL_Hash(i + float3(0,1,1)), PL_Hash(i + float3(1,1,1)), f.x), f.y), f.z);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _PATTERN_HATCH _PATTERN_HALFTONE

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
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
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            // returns 1 where ink should be drawn, for a given darkness (0 = lit, 1 = darkest)
            half InkPattern(float2 px, half darkness)
            {
                float s = max(_PatternScale, 1.0);
            #if defined(_PATTERN_HALFTONE)
                float a = radians(45.0);
                float2 r = float2(px.x * cos(a) - px.y * sin(a), px.x * sin(a) + px.y * cos(a)) / s;
                float2 cell = frac(r) - 0.5;
                float radius = sqrt(saturate(darkness)) * 0.62;
                float d = length(cell);
                float aa = fwidth(d);
                return 1.0 - smoothstep(radius - aa, radius + aa, d);
            #else
                float a = radians(_PatternAngle);
                float2 dir = float2(cos(a), sin(a));
                float line1 = abs(frac(dot(px, dir) / s) - 0.5) * 2.0;           // 0 at line center
                float w1 = saturate(darkness * 1.6) * 0.55;
                float aa1 = fwidth(line1);
                float ink = 1.0 - smoothstep(w1 - aa1, w1 + aa1, line1);
                // cross hatch in the deepest shadow
                float2 dir2 = float2(cos(a + 1.5708), sin(a + 1.5708));
                float line2 = abs(frac(dot(px, dir2) / s) - 0.5) * 2.0;
                float w2 = saturate(darkness * 1.6 - 0.8) * 0.5;
                float aa2 = fwidth(line2);
                ink = max(ink, 1.0 - smoothstep(w2 - aa2, w2 + aa2, line2));
                return ink;
            #endif
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 n = normalize(input.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = v;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                half4 shadowMask = half4(1,1,1,1);

                Light mainLight = GetMainLight(inputData.shadowCoord, input.positionWS, shadowMask);
                half shadow = lerp(1.0h, mainLight.shadowAttenuation, _ShadowReceive);
                half lit = saturate(dot(n, mainLight.direction) * 0.5h + 0.5h) * shadow;

                // hard two-tone split
                half litMask = step(_ShadeThreshold, lit);
                half3 color = albedo.rgb * lerp(_ShadowColor.rgb, mainLight.color, litMask);
                color += albedo.rgb * SampleSH(n) * _AmbientStrength;

                // ink pattern in the shadow side, denser towards the core shadow
                half darkness = saturate((_ShadeThreshold - lit) / max(_ShadeThreshold - _DeepThreshold, 0.01h));
                darkness = (1.0h - litMask) * (0.35h + 0.65h * darkness);
                half ink = InkPattern(input.positionCS.xy, darkness) * (1.0h - litMask);
                color = lerp(color, _InkColor.rgb, ink * _PatternStrength);

                // additional lights: flat colored pools (lava, torches)
                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light l = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                    half nl = step(0.1h, dot(n, l.direction));
                    half atten = step(0.18h, l.distanceAttenuation * l.shadowAttenuation) * 0.6h
                               + step(0.45h, l.distanceAttenuation * l.shadowAttenuation) * 0.4h;
                    color += albedo.rgb * l.color * nl * atten;
                LIGHT_LOOP_END
                #endif

                // hard comic rim (only on the lit side)
                half rim = 1.0h - saturate(dot(n, v));
                rim = step(1.0h - _RimPower, rim) * litMask;
                color += _RimColor.rgb * rim * _RimStrength;

                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
                half pulse = 1.0h - _EmissionPulse * (0.5h + 0.5h * sin(_Time.y * _EmissionPulseSpeed));
                color += emission * pulse;

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float3 smoothTS   : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float  fogFactor  : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 nOS = input.normalOS;
                if (_UseSmoothNormals > 0.5 && dot(input.smoothTS, input.smoothTS) > 0.01)
                {
                    float3 t = normalize(input.tangentOS.xyz);
                    float3 b = cross(nOS, t) * input.tangentOS.w;
                    nOS = normalize(t * input.smoothTS.x + b * input.smoothTS.y + nOS * input.smoothTS.z);
                }

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float weight = lerp(1.0, 0.35 + 1.3 * PL_Noise(positionWS * 2.2), _OutlineJitter);

                float4 posCS = TransformWorldToHClip(positionWS);
                float3 nWS = TransformObjectToWorldNormal(nOS);
                float2 nCS = mul((float3x3)UNITY_MATRIX_VP, nWS).xy;
                float len = length(nCS);
                if (len > 1e-5)
                    posCS.xy += (nCS / len) / _ScreenParams.xy * _OutlineWidth * weight * 2.0 * posCS.w;
                o.positionCS = posCS;
                o.fogFactor = ComputeFogFactor(posCS.z);
                return o;
            }

            half4 frag (Varyings input) : SV_Target
            {
                clip(_OutlineWidth - 0.001);
                return half4(MixFog(_OutlineColor.rgb, input.fogFactor), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert (Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                return o;
            }
            half4 frag (Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert (Attributes input) { Varyings o; UNITY_SETUP_INSTANCE_ID(input); o.positionCS = TransformObjectToHClip(input.positionOS.xyz); return o; }
            half frag (Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert (Attributes input) { Varyings o; UNITY_SETUP_INSTANCE_ID(input); o.positionCS = TransformObjectToHClip(input.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(input.normalOS); return o; }
            half4 frag (Varyings input) : SV_Target { return half4(NormalizeNormalPerPixel(input.normalWS), 0.0); }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
