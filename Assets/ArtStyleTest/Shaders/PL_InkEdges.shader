// PerfectLoot ink edges (URP 17 / Unity 6): comic ink lines inside shapes and where things stand on or in front of the
// ground, drawn by ForestAtmosphereFeature after the opaques and before the height fog, so far lines sink into the mist.
// The materials' outlines trace silhouettes only; this finds the rest from the depth-normals prepass:
// - depth: a kink in the depth (the second difference), so a slope or a floor seen at an angle stays clean, but a step,
//   a ledge or a rock on the floor gets a line, only on its near side (steps lower than the smallest step get none);
// - normals: a crease sharper than the threshold.
// The quad is drawn at the prepass depth and tested against the camera's depth, so the water (not in the prepass)
// hides the riverbed's lines. Below the level the lines fade into the abyss like everything else. Settings come from
// the InkEdges volume.
Shader "Hidden/PerfectLoot/InkEdges"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest LEqual Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "InkEdges"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "PL_Abyss.hlsl"

            half4 _InkColor;        // rgb ink, a strength
            float4 _InkEdges;       // x width (px at 1080p), y depth threshold (share of the distance), z normal threshold (1 - cos), w smallest step (m)

            float EyeDepth(float2 uv) { return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams); }

            half4 frag (Varyings input, out float depthOut : SV_Depth) : SV_Target
            {
                float2 uv = input.texcoord;
                float raw = SampleSceneDepth(uv);
                depthOut = raw;
            #if UNITY_REVERSED_Z
                if (raw <= 0.0) return 0;      // Sky
            #else
                if (raw >= 1.0) return 0;
            #endif

                float2 texel = max(_InkEdges.x * _ScreenParams.y / 1080.0, 1.0) / _ScreenParams.xy;
                float2 dx = float2(texel.x, 0.0), dy = float2(0.0, texel.y);

                // Depth: how much nearer this point is than the middle of its neighbours, as a share of its distance
                float d = LinearEyeDepth(raw, _ZBufferParams);
                float kinkX = EyeDepth(uv - dx) + EyeDepth(uv + dx) - 2.0 * d;
                float kinkY = EyeDepth(uv - dy) + EyeDepth(uv + dy) - 2.0 * d;
                float kink = max(kinkX, kinkY) / d;
                half depthInk = smoothstep(_InkEdges.y, _InkEdges.y * 1.5, kink);
                depthInk *= smoothstep(_InkEdges.w, _InkEdges.w * 1.5, max(kinkX, kinkY));    // Grass tufts and pebbles keep only their own outline

                // Normals: the sharpest bend to a neighbour
                float3 n = SampleSceneNormals(uv);
                float bend = max(max(1.0 - dot(n, SampleSceneNormals(uv + dx)), 1.0 - dot(n, SampleSceneNormals(uv - dx))),
                                 max(1.0 - dot(n, SampleSceneNormals(uv + dy)), 1.0 - dot(n, SampleSceneNormals(uv - dy))));
                half normalInk = smoothstep(_InkEdges.z, _InkEdges.z * 1.3, bend);

                half ink = max(depthInk, normalInk) * _InkColor.a;
                float3 positionWS = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                ink *= 1.0h - PL_AbyssFade(positionWS.y);
                return half4(_InkColor.rgb, ink);
            }
            ENDHLSL
        }
    }
}
