// PerfectLoot retro / "HD-pixel" 3D shader (URP 17 / Unity 6)
// Meant for a low-resolution camera (see PixelArtCamera) so 3D models read as pixel art:
// - stepped lighting (3 bands) with 4x4 Bayer dithering at the band edges
// - stepped point-light pools
// - final color snapped to the 37-color Perfect Loot dark-medieval palette (same as the sprite set)
// - 1-pixel inverted-hull outline, emissive glow added after palette snap (blooms)
// SRP Batcher compatible, supports DOTS instancing (Entities Graphics).
Shader "PerfectLoot/Retro"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _ShadowColor ("Shadow Tint", Color) = (0.42,0.36,0.5,1)
        _Band1 ("Shadow Band Edge", Range(0,1)) = 0.42
        _Band2 ("Light Band Edge", Range(0,1)) = 0.72
        _DitherStrength ("Dither Strength", Range(0,0.5)) = 0.14
        _Ambient ("Ambient", Range(0,2)) = 0.5
        _PointInfluence ("Point Light Influence", Range(0,3)) = 1.2
        _ShadowReceive ("Receive Shadows", Range(0,1)) = 1
        [Toggle] _SnapToPalette ("Snap To Palette", Float) = 1

        [Header(Glow)]
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,1)
        _EmissionPulse ("Pulse Amount", Range(0,1)) = 0
        _EmissionPulseSpeed ("Pulse Speed", Range(0,10)) = 2

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.051,0.043,0.055,1)
        _OutlineWidth ("Outline Width (px)", Range(0,4)) = 1
        [Toggle] _UseSmoothNormals ("Outline Uses Smoothed Normals (UV3)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadowColor;
            half _Band1;
            half _Band2;
            half _DitherStrength;
            half _Ambient;
            half _PointInfluence;
            half _ShadowReceive;
            half _SnapToPalette;
            half4 _EmissionColor;
            half _EmissionPulse;
            half _EmissionPulseSpeed;
            half4 _OutlineColor;
            half _OutlineWidth;
            half _UseSmoothNormals;
        CBUFFER_END

        half PL_Bayer4(float2 px)
        {
            const half m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            int2 p = int2(fmod(floor(px), 4.0));
            return (m[p.y * 4 + p.x] + 0.5h) / 16.0h;
        }

        static const half3 PL_PALETTE[37] = {
            half3(0.0510, 0.0431, 0.0549), half3(0.1098, 0.0863, 0.0980), half3(0.1686, 0.1333, 0.1490), half3(0.2314, 0.1843, 0.1922),
            half3(0.3020, 0.2471, 0.2431), half3(0.3843, 0.3216, 0.3020), half3(0.4824, 0.4157, 0.3765), half3(0.5922, 0.5137, 0.4510),
            half3(0.7176, 0.6353, 0.5451), half3(0.8431, 0.7804, 0.6510), half3(0.9373, 0.8902, 0.7765), half3(0.1647, 0.1020, 0.0784),
            half3(0.2706, 0.1490, 0.1059), half3(0.3922, 0.1961, 0.1216), half3(0.5255, 0.2510, 0.1216), half3(0.6588, 0.3216, 0.1176),
            half3(0.2275, 0.0706, 0.0627), half3(0.3608, 0.1020, 0.0863), half3(0.5098, 0.1373, 0.1059), half3(0.6588, 0.1961, 0.1216),
            half3(0.2902, 0.1098, 0.0863), half3(0.4157, 0.1569, 0.1176), half3(0.5412, 0.2118, 0.1569), half3(0.6588, 0.2824, 0.2275),
            half3(0.7608, 0.3529, 0.2667), half3(0.7843, 0.3137, 0.1098), half3(0.9098, 0.4627, 0.1176), half3(1.0000, 0.6471, 0.2275),
            half3(1.0000, 0.8275, 0.4157), half3(1.0000, 0.9529, 0.7216), half3(0.1098, 0.1647, 0.1725), half3(0.1412, 0.2510, 0.2471),
            half3(0.1843, 0.3569, 0.3451), half3(0.3098, 0.5412, 0.5255), half3(0.6039, 0.8235, 0.8118), half3(0.2000, 0.2118, 0.2392),
            half3(0.3333, 0.3529, 0.3882)
        };

        // linear in, linear out; distance measured in sRGB space
        half3 PL_SnapToPalette(half3 linearColor)
        {
            half3 s = LinearToSRGB(saturate(linearColor));
            half best = 1e5h; half3 bestC = s;
            [unroll] for (int i = 0; i < 37; i++)
            {
                half3 d = s - PL_PALETTE[i];
                half dist = dot(d * d, half3(0.35h, 0.45h, 0.2h));
                if (dist < best) { best = dist; bestC = PL_PALETTE[i]; }
            }
            return SRGBToLinear(bestC);
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 n = normalize(i.normalWS);
                half3 albedo = _BaseColor.rgb;
                half dither = PL_Bayer4(i.positionCS.xy) - 0.5h;

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = n;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                half4 shadowMask = half4(1,1,1,1);

                Light mainLight = GetMainLight(inputData.shadowCoord, i.positionWS, shadowMask);
                half shadow = lerp(1.0h, mainLight.shadowAttenuation, _ShadowReceive);
                half lit = saturate(dot(n, mainLight.direction) * 0.5h + 0.5h) * shadow + dither * _DitherStrength;
                half band = lit < _Band1 ? 0.0h : (lit < _Band2 ? 0.55h : 1.0h);
                half3 color = albedo * lerp(_ShadowColor.rgb, mainLight.color, band);
                color += albedo * SampleSH(n) * _Ambient;

                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, shadowMask);
                    half a = l.distanceAttenuation * l.shadowAttenuation * saturate(dot(n, l.direction) + 0.25h);
                    a = floor(saturate(a * 1.6h + dither * 0.3h) * 3.0h) / 3.0h;
                    color += albedo * l.color * a * _PointInfluence;
                LIGHT_LOOP_END
                #endif

                color = MixFog(color, i.fogFactor);
                if (_SnapToPalette > 0.5h) color = PL_SnapToPalette(color);

                half pulse = 1.0h - _EmissionPulse * (0.5h + 0.5h * sin(_Time.y * _EmissionPulseSpeed));
                color += _EmissionColor.rgb * pulse;
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float3 smoothTS : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float fogFactor : TEXCOORD0; };
            Varyings vert (Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 nOS = v.normalOS;
                if (_UseSmoothNormals > 0.5 && dot(v.smoothTS, v.smoothTS) > 0.01)
                {
                    float3 t = normalize(v.tangentOS.xyz);
                    float3 b = cross(nOS, t) * v.tangentOS.w;
                    nOS = normalize(t * v.smoothTS.x + b * v.smoothTS.y + nOS * v.smoothTS.z);
                }
                float4 posCS = TransformObjectToHClip(v.positionOS.xyz);
                float2 nCS = mul((float3x3)UNITY_MATRIX_VP, TransformObjectToWorldNormal(nOS)).xy;
                float len = length(nCS);
                if (len > 1e-5) posCS.xy += (nCS / len) / _ScreenParams.xy * _OutlineWidth * 2.0 * posCS.w;
                o.positionCS = posCS; o.fogFactor = ComputeFogFactor(posCS.z);
                return o;
            }
            half4 frag (Varyings i) : SV_Target { clip(_OutlineWidth - 0.001); return half4(MixFog(_OutlineColor.rgb, i.fogFactor), 1.0h); }
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
            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
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
            half4 frag (Varyings i) : SV_Target { return 0; }
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
            Varyings vert (Attributes v) { Varyings o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half frag (Varyings i) : SV_Target { return i.positionCS.z; }
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
            Varyings vert (Attributes v) { Varyings o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag (Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0.0); }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
