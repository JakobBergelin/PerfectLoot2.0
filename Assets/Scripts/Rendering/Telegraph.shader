// The ground marker of a telegraphed enemy skill (TelegraphSystems.cs). A decal: the marker is a box, and each pixel finds
// the surface behind it from the depth texture, so a circle, line or cone lies on slopes and steps. An outline shows the
// whole area and a fill grows until the skill goes off. Needs the depth texture on in the URP asset. Skips pixels that
// TelegraphMask.shader flagged, so units and walls aren't painted.
// SRP Batcher compatible, supports DOTS instancing (Entities Graphics).
Shader "PerfectLoot/Telegraph"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 0.15, 0.1, 1)
        _OutlineWidth ("Outline Width (m)", Range(0, 0.5)) = 0.12
        _EmptyAlpha ("Alpha Before Filled", Range(0, 1)) = 0.12
        _FilledAlpha ("Alpha When Filled", Range(0, 1)) = 0.5
        // x: fill. y: shape (1 circle, 2 line, 3 cone). z: line half width or cone half angle in radians.
        // w: how far above and below its center it reaches. Lengths: 1 = the telegraph's Size.
        _Telegraph ("Telegraph", Vector) = (0, 1, 0, 1)
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
            // The box's far side, whatever is in front of it: it still shows with the camera close to or inside the box
            ZTest Always
            Cull Front

            Stencil
            {
                Ref 1
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            // The surface this pixel shows, in the marker's own space
            float3 SurfacePosition (float4 positionCS)
            {
                float2 screenUV = GetNormalizedScreenSpaceUV(positionCS);
                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(screenUV);
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(screenUV));
                #endif
                return TransformWorldToObject(ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP));
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 telegraph = _Telegraph;
                float fill = telegraph.x;
                int shape = (int)round(telegraph.y);

                float3 surface = SurfacePosition(input.positionCS) * 2.0;   // The box is 1 wide: now 1 = Size
                clip(telegraph.w - abs(surface.y));
                float2 p = surface.xz;  // +y is forward
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

                // The marker's scale is twice its Size. The outline is in meters, so a long thin line still has an inside to fill.
                float size = length(GetObjectToWorldMatrix()._m00_m10_m20) * 0.5;
                float alpha = progress <= fill ? _FilledAlpha : _EmptyAlpha;
                if (edge * size < _OutlineWidth) alpha = 1.0;
                return half4(_BaseColor.rgb, alpha * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
