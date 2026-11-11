// PerfectLoot fog sky (URP 17 / Unity 6): a skybox that is the scene's fog color (RenderSettings.fogColor), a little
// darker straight down and lighter toward the horizon. With it, whatever lies beyond the level (a chasm, the edge of
// the map) fades into the same fog as distant geometry, so the top-down camera never shows a gray void.
Shader "PerfectLoot/FogSky"
{
    Properties
    {
        _HorizonLift ("Horizon Lift", Range(0,1)) = 0.25
        _DepthDarken ("Darken Below", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _HorizonLift;
                half _DepthDarken;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings vert (Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.direction = input.positionOS.xyz;
                return o;
            }

            half4 frag (Varyings input) : SV_Target
            {
                float y = normalize(input.direction).y;
                half3 fog = unity_FogColor.rgb;
                half3 color = fog * lerp(1.0h + _HorizonLift, 1.0h - _DepthDarken, saturate(-y * 1.5));
                color = lerp(color, fog * (1.0h + _HorizonLift * 0.5h), saturate(y * 2.0));
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
