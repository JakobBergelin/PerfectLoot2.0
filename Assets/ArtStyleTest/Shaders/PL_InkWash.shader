// PerfectLoot ink wash (sumi-e) shader (URP 17 / Unity 6)
// - surfaces become ink tones on paper: value comes from the base color's brightness
// - brushy, uneven shading bands (noise-distorted thresholds), paper grain in screen space
// - _ColorKeep: 0 = pure ink (world), 1 = keep full color (player, trait parts)
// - rough brush outline with varying weight
// SRP Batcher compatible, supports DOTS instancing (Entities Graphics).
Shader "PerfectLoot/InkWash"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _ColorKeep ("Keep Color (0 ink, 1 color)", Range(0,1)) = 0

        [Header(Ink and Paper)]
        _InkColor ("Ink", Color) = (0.09,0.08,0.08,1)
        _PaperColor ("Paper", Color) = (0.93,0.89,0.81,1)
        _ToneRange ("Tone Range", Range(0,1)) = 0.75
        _WashThreshold ("Wash Threshold", Range(0,1)) = 0.52
        _WashSoftness ("Wash Edge Softness", Range(0.001,0.3)) = 0.06
        _BrushScale ("Brush Noise Scale", Range(0.1,10)) = 2.2
        _BrushJitter ("Brush Jitter", Range(0,0.6)) = 0.22
        _GrainStrength ("Paper Grain", Range(0,0.5)) = 0.12
        _ShadowReceive ("Receive Shadows", Range(0,1)) = 1

        [Header(Trait Glow)]
        _EmissionMap ("Emission Mask", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _EmissionPulse ("Pulse Amount", Range(0,1)) = 0
        _EmissionPulseSpeed ("Pulse Speed", Range(0,10)) = 2
        _LightColorKeep ("Colored Light Pools (lava)", Range(0,1)) = 0.6

        [Header(Brush Outline)]
        _OutlineColor ("Outline Color", Color) = (0.07,0.06,0.06,1)
        _OutlineWidth ("Outline Width (px)", Range(0,12)) = 4
        _OutlineJitter ("Brush Weight Variation", Range(0,1)) = 0.8
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
            half _ColorKeep;
            half4 _InkColor;
            half4 _PaperColor;
            half _ToneRange;
            half _WashThreshold;
            half _WashSoftness;
            half _BrushScale;
            half _BrushJitter;
            half _GrainStrength;
            half _ShadowReceive;
            float4 _EmissionMap_ST;
            half4 _EmissionColor;
            half _EmissionPulse;
            half _EmissionPulseSpeed;
            half _LightColorKeep;
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

        float PL_Fbm(float3 p)
        {
            return PL_Noise(p) * 0.55 + PL_Noise(p * 2.13) * 0.3 + PL_Noise(p * 4.7) * 0.15;
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

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 n = normalize(input.normalWS);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                half4 shadowMask = half4(1,1,1,1);

                Light mainLight = GetMainLight(inputData.shadowCoord, input.positionWS, shadowMask);
                half shadow = lerp(1.0h, mainLight.shadowAttenuation, _ShadowReceive);
                half lit = saturate(dot(n, mainLight.direction) * 0.5h + 0.5h) * shadow;

                // brushy wash edge
                float brush = PL_Fbm(input.positionWS * _BrushScale) - 0.5;
                half wash = smoothstep(_WashThreshold - _WashSoftness, _WashThreshold + _WashSoftness, lit + brush * _BrushJitter);
                // secondary dry-brush band inside the shadow
                half dry = smoothstep(_WashThreshold * 0.45 - _WashSoftness, _WashThreshold * 0.45 + _WashSoftness, lit + brush * _BrushJitter * 1.4);

                // ink tone from the base color's brightness
                half lum = dot(albedo.rgb, half3(0.299h, 0.587h, 0.114h));
                half valueLit = lerp(1.0h - _ToneRange, 1.0h, saturate(lum * 1.35h));
                half valueShadow = valueLit * lerp(0.35h, 0.62h, dry);
                half value = lerp(valueShadow, valueLit, wash);
                half3 inkTone = lerp(_InkColor.rgb, _PaperColor.rgb, value);

                // colored version (player / traits): same wash structure, real hue
                half3 colorTone = albedo.rgb * lerp(0.45h + 0.2h * dry, 1.05h, wash) * lerp(_PaperColor.rgb, 1.0h, 0.5h);
                half3 color = lerp(inkTone, colorTone, _ColorKeep);

                // colored light pools (lava etc.) bleed through like watercolor
                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light l = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                    half atten = l.distanceAttenuation * l.shadowAttenuation * saturate(dot(n, l.direction) + 0.3h);
                    atten = smoothstep(0.05h, 0.5h, atten + brush * 0.15h);
                    color = lerp(color, color * 0.5h + l.color * 0.35h, atten * _LightColorKeep);
                LIGHT_LOOP_END
                #endif

                // paper grain (screen space) and soft fibers
                float2 px = input.positionCS.xy;
                float grain = PL_Noise(float3(px * 0.9, 0.0)) * 0.6 + PL_Noise(float3(px * 0.12, 3.1)) * 0.4;
                color *= 1.0h - _GrainStrength * (grain - 0.5h) * 2.0h * (1.0h - _ColorKeep * 0.5h);

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
                float  dryBrush   : TEXCOORD1;
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
                float nz = PL_Fbm(positionWS * 1.6);
                float weight = lerp(1.0, 0.15 + 1.7 * nz, _OutlineJitter);   // brush pressure
                o.dryBrush = nz;

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
                // dry-brush breaks in the stroke
                float breaks = PL_Noise(float3(input.positionCS.xy * 0.35, input.dryBrush * 7.0));
                clip(breaks - 0.18);
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
