// PerfectLoot cloud sky (URP 17 / Unity 6): a comic sky for views that see the horizon (the main menu's clearing).
// A flat two-band gradient from the zenith colour down to the scene's fog colour at the horizon, so distant geometry
// still fades into the same fog as the sky behind it and the scene keeps its depth. Around it stand cartoon cumulus
// clouds, laid out by angle (around and above the viewer) rather than on a flat plane, so they keep their shape all the
// way down to the horizon: each a row of round puffs, tallest in the middle, with a flat base. Flat cel shading (a warm
// lit top, cool shade on the underside of every puff, toward the base) and an ink outline. Two layers: a far one, smaller and
// fading into the fog, and a near one; both drift slowly around. Below the horizon it turns to the abyss colour while a
// level has a backdrop (PL_Abyss.hlsl), like PerfectLoot/FogSky.
Shader "PerfectLoot/CloudSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.42, 0.66, 0.74, 1)
        _MidColor ("Mid Sky", Color) = (0.58, 0.78, 0.78, 1)
        _BandHeight ("Band Height", Range(0, 1)) = 0.32
        _HorizonFade ("Horizon Fade", Range(0.01, 0.5)) = 0.12

        _CloudLit ("Cloud Lit", Color) = (1.0, 0.97, 0.88, 1)
        _CloudShade ("Cloud Shade", Color) = (0.70, 0.83, 0.84, 1)
        _InkColor ("Ink", Color) = (0.07, 0.047, 0.086, 1)
        _InkWidth ("Ink Width", Range(0, 0.1)) = 0.022
        _Coverage ("Coverage", Range(0, 1)) = 0.55
        _CloudSize ("Cloud Size", Range(0.3, 3)) = 1.0
        _CloudLow ("Lowest Base (deg)", Range(0, 30)) = 4
        _CloudHigh ("Highest Base (deg)", Range(0, 60)) = 22
        _ShadeBand ("Shade Depth", Range(0, 1)) = 0.22
        _Drift ("Drift (deg/s)", Float) = 0.4
        _FarFade ("Far Layer Fog", Range(0, 1)) = 0.45
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
            #include "PL_Abyss.hlsl"
            #include "PL_Canopy.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _MidColor, _CloudLit, _CloudShade, _InkColor;
                half _BandHeight, _HorizonFade, _InkWidth, _Coverage, _CloudSize, _CloudLow, _CloudHigh, _ShadeBand, _Drift, _FarFade;
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

            // One layer of cumulus at q = (degrees around, degrees up). Cells of CellWidth degrees around the sky hold at
            // most one cloud each. Returns how far inside a cloud's outline (degrees, negative outside), and through
            // base and height the cloud q is nearest, for the shading.
            float Cumulus(float2 q, float cellWidth, float size, float seed, out float base, out float height)
            {
                base = 0.0;
                height = 1.0;
                float best = -1e3;
                float cell = floor(q.x / cellWidth);
                for (int c = -1; c <= 1; c++)
                {
                    float id = cell + c + seed;
                    if (PL_Hash(float2(id, 3.7)) > _Coverage) continue;
                    float width = cellWidth * lerp(0.55, 0.95, PL_Hash(float2(id, 9.1))) * size;
                    float tall = width * lerp(0.28, 0.45, PL_Hash(float2(id, 5.3)));
                    float b = lerp(_CloudLow, _CloudHigh, PL_Hash(float2(id, 1.9)));
                    float middle = (cell + c + 0.5) * cellWidth + (PL_Hash(float2(id, 7.7)) - 0.5) * cellWidth * 0.3;

                    // Puffs along the cloud, tallest in the middle, their bottoms cut flat at the base
                    float inside = -1e3;
                    for (int i = 0; i < 6; i++)
                    {
                        float t = (i + 0.5) / 6.0 * 2.0 - 1.0;
                        float bump = 1.0 - t * t * 0.75;
                        float radius = tall * bump * lerp(0.42, 0.62, PL_Hash(float2(id, i * 2.3 + 0.4)));
                        float2 center = float2(middle + t * width * 0.42, b + radius * lerp(0.35, 0.85, PL_Hash(float2(id, i * 4.1))));
                        inside = max(inside, radius - length(q - center));
                    }
                    // A crown puff on top
                    float crown = tall * 0.48;
                    inside = max(inside, crown - length(q - float2(middle + (PL_Hash(float2(id, 2.2)) - 0.5) * width * 0.25, b + tall * 0.55)));
                    inside = min(inside, q.y - b);
                    if (inside > best)
                    {
                        best = inside;
                        base = b;
                        height = tall + crown;
                    }
                }
                return best;
            }

            // A layer's colour over color: lit top, a scalloped cool band at the base, ink outline, fog by amount
            half3 Layer(half3 color, half3 fog, float2 q, float cellWidth, float size, float seed, half fogAmount)
            {
                float base, height;
                float inside = Cumulus(q, cellWidth, size, seed, base, height);
                float aa = fwidth(inside) + 1e-4;
                half cloud = smoothstep(-aa, aa, inside);
                if (cloud <= 0.0) return color;

                // Shaded where a step down and left (away from the sun, up and right) leaves the cloud: the base and the
                // lower side of every puff
                float b2, h2;
                float below = Cumulus(q + float2(-0.35, -1.0) * height * _ShadeBand, cellWidth, size, seed, b2, h2);
                half shade = smoothstep(aa, -aa, below);
                half3 c = lerp(_CloudLit.rgb, _CloudShade.rgb, shade);
                half ink = smoothstep(_InkWidth * height + aa, _InkWidth * height - aa, inside);
                c = lerp(c, _InkColor.rgb, ink * 0.85h);
                c = lerp(c, fog, fogAmount);
                return lerp(color, c, cloud);
            }

            half4 frag (Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                float y = dir.y;
                half3 fog = unity_FogColor.rgb;

                // Sky: fog at the horizon, then two flat bands up to the zenith (hard steps, like the comic shading)
                half3 sky = y > _BandHeight ? _ZenithColor.rgb : _MidColor.rgb;
                half3 color = lerp(fog, sky, smoothstep(0.0, _HorizonFade, y));

                if (y > -0.02)
                {
                    // Degrees around (drifting) and up
                    float around = degrees(atan2(dir.x, dir.z));
                    float up = degrees(asin(saturate(y)));
                    float drift = _Drift * _Time.y;
                    // Far layer: smaller, lower and half in the fog; near layer: big, over it
                    color = Layer(color, fog, float2(around + drift * 0.5, up * 1.5), 20.0, _CloudSize * 0.7, 51.0, _FarFade);
                    color = Layer(color, fog, float2(around + drift, up), 30.0, _CloudSize * 0.85, 0.0, 0.05h);
                }

                // Below a level with a backdrop: the abyss color, the same as everything deep down (PL_Abyss.hlsl)
                if (y < 0.0 && _PL_AbyssDepth > 0.0) color = lerp(color, _PL_AbyssColor.rgb, saturate(-y * 4.0));
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
