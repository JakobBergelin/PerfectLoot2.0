// PerfectLoot tilt-shift (URP 17 / Unity 6): a miniature-diorama blur at the top and bottom of the screen, drawn by
// ForestAtmosphereFeature before post-processing. One pass: a small golden-angle disk of taps whose radius grows from 0 in
// the sharp middle band to full strength at the edges. Settings come from the TiltShift volume.
Shader "Hidden/PerfectLoot/TiltShift"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "TiltShift"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _TiltShift;  // x blur in pixels at 1080p, y share of the height that stays sharp, z fade, w unused

            #define TAPS 16

            half4 frag (Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float fromMiddle = abs(uv.y - 0.5) * 2.0;
                float blur = smoothstep(_TiltShift.y, _TiltShift.y + _TiltShift.z, fromMiddle);
                float radius = _TiltShift.x * blur * (_ScreenParams.y / 1080.0);
                if (radius < 0.25) return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);

                // Taps spread evenly over a disk (golden angle), weighted the same
                float2 texel = radius / _ScreenParams.xy;
                half4 sum = 0;
                [unroll] for (int i = 0; i < TAPS; i++)
                {
                    float r = sqrt((i + 0.5) / TAPS);
                    float a = i * 2.39996323;
                    sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(cos(a), sin(a)) * r * texel, 0);
                }
                return sum / TAPS;
            }
            ENDHLSL
        }
    }
}
